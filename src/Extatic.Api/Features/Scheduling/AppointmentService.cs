using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Features.Scheduling.SchedulingValidation;

namespace Extatic.Api.Features.Scheduling;

public record BookingInput(DateTime? StartsAt, string? Name, string? Email, string? Phone, string? Timezone, JsonElement? Data);

public record AppointmentFilter(DateTime? From, DateTime? To, Guid? CalendarId, Guid? TypeId, string? Status);

/// <summary>
/// Slot queries, booking and the appointment lifecycle. Double booking is prevented by the
/// <see cref="SchedulingSchema"/> exclusion constraint (SQLSTATE 23P01 → 409); the requested start is
/// always re-validated against freshly generated slots inside the booking transaction. When a type
/// has MaxPerDay, bookings of that type are serialised with a row lock on the type so the daily cap
/// holds under concurrency. Status changes lock the appointment row (SELECT … FOR UPDATE).
/// </summary>
public class AppointmentService(
    AppDbContext db,
    JsonSchemaValidator validator,
    SchedulingNotifier notifier,
    TimeProvider time)
{
    public const string SlotUnavailable = "That time slot is no longer available";
    private static readonly AppointmentStatus[] ActiveStatuses = [AppointmentStatus.Pending, AppointmentStatus.Confirmed];

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    // ---------- Types & slots ----------

    public Task<List<AppointmentType>> ListPublicTypesAsync(Guid appId)
        => db.AppointmentTypes.AsNoTracking().Include(t => t.Calendar)
            .Where(t => t.AppId == appId && t.IsActive && t.Calendar.IsActive)
            .OrderBy(t => t.Name).ToListAsync();

    public async Task<AppointmentType> GetPublicTypeAsync(Guid appId, string slug)
        => await db.AppointmentTypes.Include(t => t.Calendar)
               .FirstOrDefaultAsync(t => t.AppId == appId && t.Slug == slug && t.IsActive && t.Calendar.IsActive)
           ?? throw new NotFoundException("Appointment type not found");

    /// <summary>
    /// Slots in [from, to) local dates of <paramref name="timezoneId"/> (default: calendar zone), grouped
    /// by local date in that zone. Max <see cref="SlotEngine.MaxRangeDays"/> days.
    /// </summary>
    public async Task<SlotsResponse> GetSlotsAsync(AppointmentType type, DateOnly? from, DateOnly? to, string? timezoneId,
        bool enforceMinNotice = true)
    {
        var calendarZone = RequireZone(type.Calendar.Timezone, "timezone");
        var outputZone = string.IsNullOrWhiteSpace(timezoneId) ? calendarZone : RequireZone(timezoneId, "timezone");
        var fromDate = from ?? throw new BadRequestException("from is required (YYYY-MM-DD)");
        var toDate = to ?? throw new BadRequestException("to is required (YYYY-MM-DD)");
        if (toDate <= fromDate || toDate.DayNumber - fromDate.DayNumber > SlotEngine.MaxRangeDays)
            throw new BadRequestException($"to must be after from, and the range at most {SlotEngine.MaxRangeDays} days");

        var fromUtc = SlotEngine.StartOfLocalDateUtc(fromDate, outputZone);
        var toUtc = SlotEngine.StartOfLocalDateUtc(toDate, outputZone);
        var slots = await ComputeSlotsAsync(type, SlotSettings.From(type), fromUtc, toUtc, enforceMinNotice, excludeAppointmentId: null);

        var days = slots
            .Select(s => (Slot: s, Local: TimeZoneInfo.ConvertTimeFromUtc(s.StartsAt, outputZone)))
            .GroupBy(x => DateOnly.FromDateTime(x.Local))
            .Select(g => new SlotDayDto(g.Key, g.Select(x => new SlotDto(x.Slot.StartsAt, x.Slot.EndsAt,
                x.Local.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList()))
            .ToList();
        return new SlotsResponse(outputZone.Id, days);
    }

    /// <summary>Generic availability preview for a calendar (dashboard), using an ad-hoc duration.</summary>
    public async Task<SlotsResponse> PreviewCalendarSlotsAsync(BookingCalendar calendar, int durationMinutes, DateOnly? from, DateOnly? to)
    {
        if (durationMinutes is < 5 or > 1440) throw new BadRequestException("duration must be between 5 and 1440");
        var pseudo = new AppointmentType
        {
            Id = Guid.Empty, AppId = calendar.AppId, CalendarId = calendar.Id, Calendar = calendar,
            DurationMinutes = durationMinutes, SlotIntervalMinutes = durationMinutes,
            MinNoticeMinutes = 0, MaxAdvanceDays = 730
        };
        return await GetSlotsAsync(pseudo, from, to, null, enforceMinNotice: false);
    }

    private async Task<List<Slot>> ComputeSlotsAsync(AppointmentType type, SlotSettings settings, DateTime fromUtc, DateTime toUtc,
        bool enforceMinNotice, Guid? excludeAppointmentId)
    {
        var zone = RequireZone(type.Calendar.Timezone, "timezone");
        var appId = type.AppId;
        var calendarId = type.CalendarId;
        // Local dates touched by the range, padded by a day for zone offsets.
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone)).AddDays(-1);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone)).AddDays(1);
        var busyFrom = fromUtc.AddDays(-2);
        var busyTo = toUtc.AddDays(2);

        var rules = await db.AvailabilityRules.AsNoTracking()
            .Where(r => r.AppId == appId && r.CalendarId == calendarId).ToListAsync();
        var overrides = await db.AvailabilityOverrides.AsNoTracking()
            .Where(o => o.AppId == appId && o.CalendarId == calendarId && o.Date >= firstDate && o.Date <= lastDate).ToListAsync();
        var active = db.Appointments.AsNoTracking()
            .Where(a => a.AppId == appId && a.CalendarId == calendarId && ActiveStatuses.Contains(a.Status)
                && a.BlockEndsAt > busyFrom && a.BlockStartsAt < busyTo);
        if (excludeAppointmentId is { } exclude) active = active.Where(a => a.Id != exclude);
        var rows = await active.Select(a => new { a.BlockStartsAt, a.BlockEndsAt, a.StartsAt, a.AppointmentTypeId }).ToListAsync();

        var busy = rows.Select(r => new BusyBlock(AsUtc(r.BlockStartsAt), AsUtc(r.BlockEndsAt)));
        var sameType = rows.Where(r => r.AppointmentTypeId == type.Id).Select(r => AsUtc(r.StartsAt));
        return SlotEngine.Generate(zone, rules, overrides, settings, busy, sameType, fromUtc, toUtc, UtcNow, enforceMinNotice);
    }

    /// <summary>The slot starting exactly at <paramref name="startUtc"/>, or 409.</summary>
    private async Task<Slot> RequireSlotAsync(AppointmentType type, DateTime startUtc, bool enforceMinNotice, Guid? excludeAppointmentId)
    {
        var slots = await ComputeSlotsAsync(type, SlotSettings.From(type), startUtc, startUtc.AddMinutes(1), enforceMinNotice, excludeAppointmentId);
        return slots.FirstOrDefault(s => s.StartsAt == startUtc) ?? throw new ConflictException(SlotUnavailable);
    }

    // ---------- Booking ----------

    /// <summary>
    /// Creates an appointment. Customer bookings (<paramref name="byAdmin"/> false) honour MinNotice and
    /// RequiresApproval. Admin bookings bypass MinNotice (never the overlap constraint) and may be created
    /// Confirmed. Returns the appointment and its one-time manage token.
    /// </summary>
    public async Task<(Appointment Appointment, string ManageToken)> BookAsync(AppointmentType type, BookingInput input,
        Guid? appUserId, bool byAdmin = false, bool? confirm = null, string? notes = null)
    {
        var startUtc = AsUtc(input.StartsAt ?? throw Invalid("starts_at", "Required (UTC ISO 8601)"));
        if (startUtc.Ticks % TimeSpan.TicksPerMinute != 0) throw new ConflictException(SlotUnavailable);
        var name = RequireText(input.Name, "name", 200);
        var email = RequireEmail(input.Email, "email");
        var phone = OptionalText(input.Phone, "phone", 50);
        string? customerZone = null;
        if (!string.IsNullOrWhiteSpace(input.Timezone)) customerZone = RequireZone(input.Timezone, "timezone").Id;
        var data = ValidateData(type, input.Data);

        // The navigations below must not be inserted as new rows.
        if (db.Entry(type).State == EntityState.Detached) db.Attach(type);

        var token = Tokens.NewUrlToken();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (type.MaxPerDay is not null) await LockTypeAsync(type);
        var slot = await RequireSlotAsync(type, startUtc, enforceMinNotice: !byAdmin, excludeAppointmentId: null);

        var now = UtcNow;
        var status = byAdmin
            ? (confirm ?? true) ? AppointmentStatus.Confirmed : AppointmentStatus.Pending
            : type.RequiresApproval ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;
        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(),
            AppId = type.AppId,
            CalendarId = type.CalendarId,
            AppointmentTypeId = type.Id,
            AppUserId = appUserId,
            CustomerName = name,
            CustomerEmail = email,
            CustomerPhone = phone,
            CustomerTimezone = customerZone,
            StartsAt = slot.StartsAt,
            EndsAt = slot.EndsAt,
            BlockStartsAt = slot.BlockStartsAt,
            BlockEndsAt = slot.BlockEndsAt,
            Status = status,
            Data = data,
            Notes = OptionalText(notes, "notes", 5000, singleLine: false),
            ManageTokenHash = Tokens.Hash(token),
            AppointmentType = type,
            Calendar = type.Calendar
        };
        SetReminderBaseline(appointment, now);
        db.Appointments.Add(appointment);

        await notifier.StageWebhookAsync(db, appointment, SchedulingEvents.Booked);
        if (status == AppointmentStatus.Confirmed) await notifier.StageWebhookAsync(db, appointment, SchedulingEvents.Confirmed);
        notifier.StageCustomerEmail(db, appointment, AppointmentEmailKind.Booked, token);
        notifier.StageStaffEmails(db, appointment, AppointmentEmailKind.Booked);

        await SaveOrConflictAsync();
        await tx.CommitAsync();
        return (appointment, token);
    }

    private string ValidateData(AppointmentType type, JsonElement? input)
    {
        var data = input is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } d ? d : JsonDocument.Parse("{}").RootElement;
        if (data.ValueKind != JsonValueKind.Object) throw Invalid("data", "Must be a JSON object");
        var raw = data.GetRawText();
        if (raw.Length > 64 * 1024) throw Invalid("data", "At most 64 KB");
        if (type.IntakeSchema is { } schema)
        {
            var errors = validator.Validate(schema, data);
            if (errors.Count > 0) throw new ValidationException("Intake answers do not match the schema", errors);
        }
        return raw;
    }

    // ---------- Lookup ----------

    public async Task<Appointment> GetByTokenAsync(Guid appId, string token)
    {
        var hash = HashToken(token);
        return await WithRefs(db.Appointments.AsNoTracking()).FirstOrDefaultAsync(a => a.AppId == appId && a.ManageTokenHash == hash)
               ?? throw new NotFoundException("Appointment not found");
    }

    public async Task<Appointment> GetAsync(Guid appId, Guid id)
        => await WithRefs(db.Appointments.AsNoTracking()).FirstOrDefaultAsync(a => a.AppId == appId && a.Id == id)
           ?? throw new NotFoundException("Appointment not found");

    public Task<List<Appointment>> ListForAppUserAsync(Guid appId, Guid appUserId)
        => WithRefs(db.Appointments.AsNoTracking())
            .Where(a => a.AppId == appId && a.AppUserId == appUserId)
            .OrderByDescending(a => a.StartsAt).Take(200).ToListAsync();

    public async Task<PagedResult<AppointmentDto>> ListAsync(Guid appId, AppointmentFilter filter, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = Filter(appId, filter);
        var total = await query.CountAsync();
        var items = await query.OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<AppointmentDto>(items.Select(a => a.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<byte[]> ExportCsvAsync(Guid appId, AppointmentFilter filter)
    {
        var rows = await Filter(appId, filter).OrderBy(a => a.StartsAt).Take(10_000).ToListAsync();
        var csv = new CsvBuilder();
        csv.AddRow("id", "status", "calendar", "type", "starts_at_utc", "ends_at_utc", "starts_at_local", "timezone",
            "customer_name", "customer_email", "customer_phone", "customer_timezone", "data", "notes", "cancel_reason", "created_at_utc");
        foreach (var a in rows)
        {
            var zone = SchedulingNotifier.CalendarZone(a);
            var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(a.StartsAt), zone);
            csv.AddRow(a.Id, a.Status.ToString(), a.Calendar.Name, a.AppointmentType.Name,
                AsUtc(a.StartsAt).ToString("o", CultureInfo.InvariantCulture), AsUtc(a.EndsAt).ToString("o", CultureInfo.InvariantCulture),
                local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), zone.Id,
                a.CustomerName, a.CustomerEmail, a.CustomerPhone, a.CustomerTimezone, a.Data, a.Notes, a.CancelReason,
                AsUtc(a.CreatedAt).ToString("o", CultureInfo.InvariantCulture));
        }
        return csv.ToBytes();
    }

    private IQueryable<Appointment> Filter(Guid appId, AppointmentFilter f)
    {
        var query = WithRefs(db.Appointments.AsNoTracking()).Where(a => a.AppId == appId);
        if (f.From is { } from) { var u = AsUtc(from); query = query.Where(a => a.StartsAt >= u); }
        if (f.To is { } to) { var u = AsUtc(to); query = query.Where(a => a.StartsAt < u); }
        if (f.CalendarId is { } c) query = query.Where(a => a.CalendarId == c);
        if (f.TypeId is { } t) query = query.Where(a => a.AppointmentTypeId == t);
        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            var statuses = new List<AppointmentStatus>();
            foreach (var part in f.Status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse<AppointmentStatus>(part, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                    throw new BadRequestException("status must be Pending, Confirmed, Cancelled, Completed or NoShow");
                statuses.Add(s);
            }
            query = query.Where(a => statuses.Contains(a.Status));
        }
        return query;
    }

    // ---------- Customer actions (by manage token) ----------

    public async Task<Appointment> CancelByCustomerAsync(Guid appId, string token, string? reason)
    {
        var hash = HashToken(token);
        return await CancelAsync(appId, a => a.ManageTokenHash == hash, reason, byCustomer: true);
    }

    public async Task<Appointment> RescheduleByCustomerAsync(Guid appId, string token, DateTime? startsAt)
    {
        var hash = HashToken(token);
        return await RescheduleAsync(appId, a => a.ManageTokenHash == hash, startsAt, byCustomer: true);
    }

    // ---------- Admin actions ----------

    public Task<Appointment> CancelByAdminAsync(Guid appId, Guid id, string? reason)
        => CancelAsync(appId, a => a.Id == id, reason, byCustomer: false);

    public Task<Appointment> RescheduleByAdminAsync(Guid appId, Guid id, DateTime? startsAt)
        => RescheduleAsync(appId, a => a.Id == id, startsAt, byCustomer: false);

    public Task<Appointment> ConfirmAsync(Guid appId, Guid id)
        => TransitionAsync(appId, id, [AppointmentStatus.Pending], AppointmentStatus.Confirmed, async a =>
        {
            SetReminderBaseline(a, UtcNow);
            await notifier.StageWebhookAsync(db, a, SchedulingEvents.Confirmed);
            notifier.StageCustomerEmail(db, a, AppointmentEmailKind.Approved);
        });

    public Task<Appointment> DeclineAsync(Guid appId, Guid id, string? reason)
        => TransitionAsync(appId, id, [AppointmentStatus.Pending], AppointmentStatus.Cancelled, async a =>
        {
            a.CancelledAt = UtcNow;
            a.CancelReason = OptionalText(reason, "reason", 1000, singleLine: false);
            await notifier.StageWebhookAsync(db, a, SchedulingEvents.Cancelled);
            notifier.StageCustomerEmail(db, a, AppointmentEmailKind.Declined);
        });

    public Task<Appointment> CompleteAsync(Guid appId, Guid id)
        => TransitionAsync(appId, id, [AppointmentStatus.Confirmed], AppointmentStatus.Completed,
            a => notifier.StageWebhookAsync(db, a, SchedulingEvents.Completed));

    public Task<Appointment> MarkNoShowAsync(Guid appId, Guid id)
        => TransitionAsync(appId, id, [AppointmentStatus.Confirmed], AppointmentStatus.NoShow, _ => Task.CompletedTask);

    public async Task<Appointment> UpdateNotesAsync(Guid appId, Guid id, string? notes)
    {
        var a = await WithRefs(db.Appointments).FirstOrDefaultAsync(x => x.AppId == appId && x.Id == id)
                ?? throw new NotFoundException("Appointment not found");
        a.Notes = OptionalText(notes, "notes", 5000, singleLine: false);
        await db.SaveChangesAsync();
        return a;
    }

    // ---------- Shared lifecycle ----------

    private async Task<Appointment> CancelAsync(Guid appId, Expression<Func<Appointment, bool>> match, string? reason, bool byCustomer)
    {
        var cleanReason = OptionalText(reason, "reason", 1000, singleLine: false);
        var (tx, a) = await LockAsync(appId, match);
        await using (tx)
        {
            if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
                throw new ConflictException($"Cannot cancel an appointment that is {a.Status}");
            if (byCustomer) EnsureBeforeNotice(a, "cancel");
            a.Status = AppointmentStatus.Cancelled;
            a.CancelledAt = UtcNow;
            a.CancelReason = cleanReason;
            await notifier.StageWebhookAsync(db, a, SchedulingEvents.Cancelled);
            notifier.StageCustomerEmail(db, a, AppointmentEmailKind.Cancelled);
            notifier.StageStaffEmails(db, a, AppointmentEmailKind.Cancelled);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return a;
        }
    }

    /// <summary>
    /// Moves the appointment in place (one UPDATE), so the old block is freed and the new one taken
    /// atomically; any failure (invalid slot, 23P01 overlap) rolls back and leaves the original intact.
    /// </summary>
    private async Task<Appointment> RescheduleAsync(Guid appId, Expression<Func<Appointment, bool>> match, DateTime? startsAt, bool byCustomer)
    {
        var startUtc = AsUtc(startsAt ?? throw Invalid("starts_at", "Required (UTC ISO 8601)"));
        var (tx, a) = await LockAsync(appId, match);
        await using (tx)
        {
            if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
                throw new ConflictException($"Cannot reschedule an appointment that is {a.Status}");
            if (byCustomer) EnsureBeforeNotice(a, "reschedule");
            if (startUtc == a.StartsAt) return a;
            if (startUtc.Ticks % TimeSpan.TicksPerMinute != 0) throw new ConflictException(SlotUnavailable);
            if (a.AppointmentType.MaxPerDay is not null) await LockTypeAsync(a.AppointmentType);
            var slot = await RequireSlotAsync(a.AppointmentType, startUtc, enforceMinNotice: byCustomer, excludeAppointmentId: a.Id);

            var previous = a.StartsAt;
            a.StartsAt = slot.StartsAt;
            a.EndsAt = slot.EndsAt;
            a.BlockStartsAt = slot.BlockStartsAt;
            a.BlockEndsAt = slot.BlockEndsAt;
            a.ReminderSentAt = null;
            SetReminderBaseline(a, UtcNow);
            await notifier.StageWebhookAsync(db, a, SchedulingEvents.Rescheduled, previous);
            notifier.StageCustomerEmail(db, a, AppointmentEmailKind.Rescheduled, previousStartsAt: previous);
            notifier.StageStaffEmails(db, a, AppointmentEmailKind.Rescheduled, previous);
            await SaveOrConflictAsync();
            await tx.CommitAsync();
            return a;
        }
    }

    private async Task<Appointment> TransitionAsync(Guid appId, Guid id, AppointmentStatus[] from, AppointmentStatus to,
        Func<Appointment, Task> stage)
    {
        var (tx, a) = await LockAsync(appId, x => x.Id == id);
        await using (tx)
        {
            if (!from.Contains(a.Status))
                throw new ConflictException($"Cannot change an appointment from {a.Status} to {to}");
            a.Status = to;
            await stage(a);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return a;
        }
    }

    /// <summary>Opens a transaction and loads the matching appointment under a row lock (404 when absent or another App's).</summary>
    private async Task<(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction, Appointment)> LockAsync(
        Guid appId, Expression<Func<Appointment, bool>> match)
    {
        var id = await db.Appointments.AsNoTracking().Where(a => a.AppId == appId).Where(match)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync() ?? throw new NotFoundException("Appointment not found");
        var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM appointments WHERE \"Id\" = {id} AND \"AppId\" = {appId} FOR UPDATE");
            var a = await WithRefs(db.Appointments).FirstAsync(x => x.AppId == appId && x.Id == id);
            return (tx, a);
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private Task LockTypeAsync(AppointmentType type)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM appointment_types WHERE \"Id\" = {type.Id} AND \"AppId\" = {type.AppId} FOR UPDATE");

    private void EnsureBeforeNotice(Appointment a, string action)
    {
        if (a.StartsAt < UtcNow.AddMinutes(a.AppointmentType.MinNoticeMinutes))
            throw new ConflictException($"It is too late to {action} this appointment online; please contact us");
    }

    /// <summary>No reminder is needed when the appointment starts within 24 h of the booking/confirmation email.</summary>
    private static void SetReminderBaseline(Appointment a, DateTime now)
    {
        if (a.Status == AppointmentStatus.Confirmed && a.StartsAt - now <= AppointmentReminderWorker.Lead)
            a.ReminderSentAt = now;
    }

    private async Task SaveOrConflictAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsOverlapViolation(ex))
        {
            throw new ConflictException(SlotUnavailable);
        }
    }

    private static string HashToken(string token)
        => string.IsNullOrEmpty(token) || token.Length > 200 ? "-" : Tokens.Hash(token);

    private static IQueryable<Appointment> WithRefs(IQueryable<Appointment> q)
        => q.Include(a => a.Calendar).Include(a => a.AppointmentType);
}
