using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Services;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Features.Scheduling.SchedulingValidation;

namespace Extatic.Api.Features.Scheduling;

/// <summary>Calendars, weekly rules, date overrides and appointment types. Every query filters by AppId.</summary>
public class SchedulingConfigService(AppDbContext db)
{
    // ---- Calendars ----

    public Task<List<BookingCalendar>> ListCalendarsAsync(Guid appId)
        => db.BookingCalendars.Include(c => c.Rules).Where(c => c.AppId == appId).OrderBy(c => c.Name).ToListAsync();

    public async Task<BookingCalendar> GetCalendarAsync(Guid appId, Guid id)
        => await db.BookingCalendars.Include(c => c.Rules).FirstOrDefaultAsync(c => c.AppId == appId && c.Id == id)
           ?? throw new NotFoundException("Calendar not found");

    public async Task<BookingCalendar> CreateCalendarAsync(Guid appId, SaveBookingCalendarRequest req)
    {
        var calendar = new BookingCalendar { AppId = appId };
        Apply(calendar, req, creating: true);
        db.BookingCalendars.Add(calendar);
        await SaveUniqueAsync("A calendar with this slug already exists");
        return calendar;
    }

    public async Task<BookingCalendar> UpdateCalendarAsync(Guid appId, Guid id, SaveBookingCalendarRequest req)
    {
        var calendar = await GetCalendarAsync(appId, id);
        Apply(calendar, req, creating: false);
        await SaveUniqueAsync("A calendar with this slug already exists");
        return calendar;
    }

    public async Task DeleteCalendarAsync(Guid appId, Guid id)
    {
        var calendar = await GetCalendarAsync(appId, id);
        if (await db.Appointments.AnyAsync(a => a.AppId == appId && a.CalendarId == id))
            throw new ConflictException("This calendar has appointments; deactivate it instead");
        db.BookingCalendars.Remove(calendar);
        await db.SaveChangesAsync();
    }

    private static void Apply(BookingCalendar c, SaveBookingCalendarRequest req, bool creating)
    {
        if (creating || req.Name is not null) c.Name = RequireText(req.Name, "name", 200);
        if (creating || req.Slug is not null)
        {
            Slugs.EnsureValid(req.Slug);
            c.Slug = req.Slug!;
        }
        if (creating || req.Timezone is not null)
        {
            RequireZone(req.Timezone, "timezone");
            c.Timezone = req.Timezone!;
        }
        if (req.NotifyEmails is not null)
        {
            if (req.NotifyEmails.Length > 10) throw Invalid("notify_emails", "At most 10 addresses");
            c.NotifyEmails = req.NotifyEmails.Select((e, i) => RequireEmail(e, $"/notify_emails/{i}")).Distinct().ToArray();
        }
        if (req.IsActive is { } active) c.IsActive = active;
    }

    public async Task<BookingCalendar> ReplaceRulesAsync(Guid appId, Guid calendarId, ReplaceAvailabilityRulesRequest req)
    {
        var parsed = ParseRules(req.Rules ?? []);
        // Row lock serialises concurrent replace-all requests so the result is one of them, never a merge.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM booking_calendars WHERE \"Id\" = {calendarId} AND \"AppId\" = {appId} FOR UPDATE");
        await db.AvailabilityRules.Where(r => r.AppId == appId && r.CalendarId == calendarId).ExecuteDeleteAsync();
        var calendar = await db.BookingCalendars.FirstOrDefaultAsync(c => c.AppId == appId && c.Id == calendarId)
                       ?? throw new NotFoundException("Calendar not found");
        foreach (var (day, start, end) in parsed)
            db.AvailabilityRules.Add(new AvailabilityRule { AppId = appId, CalendarId = calendarId, DayOfWeek = day, StartTime = start, EndTime = end });
        db.Entry(calendar).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetCalendarAsync(appId, calendarId);
    }

    // ---- Overrides ----

    public async Task<List<AvailabilityOverride>> ListOverridesAsync(Guid appId, Guid calendarId, DateOnly? from, DateOnly? to)
    {
        await GetCalendarAsync(appId, calendarId);
        var query = db.AvailabilityOverrides.Where(o => o.AppId == appId && o.CalendarId == calendarId);
        if (from is { } f) query = query.Where(o => o.Date >= f);
        if (to is { } t) query = query.Where(o => o.Date < t);
        return await query.OrderBy(o => o.Date).ToListAsync();
    }

    public async Task<AvailabilityOverride> CreateOverrideAsync(Guid appId, Guid calendarId, SaveAvailabilityOverrideRequest req)
    {
        await GetCalendarAsync(appId, calendarId);
        var o = new AvailabilityOverride { AppId = appId, CalendarId = calendarId };
        Apply(o, req);
        db.AvailabilityOverrides.Add(o);
        await SaveUniqueAsync("An override for this date already exists");
        return o;
    }

    public async Task<AvailabilityOverride> UpdateOverrideAsync(Guid appId, Guid calendarId, Guid id, SaveAvailabilityOverrideRequest req)
    {
        var o = await db.AvailabilityOverrides.FirstOrDefaultAsync(x => x.AppId == appId && x.CalendarId == calendarId && x.Id == id)
                ?? throw new NotFoundException("Override not found");
        Apply(o, req);
        await SaveUniqueAsync("An override for this date already exists");
        return o;
    }

    public async Task DeleteOverrideAsync(Guid appId, Guid calendarId, Guid id)
    {
        var deleted = await db.AvailabilityOverrides
            .Where(x => x.AppId == appId && x.CalendarId == calendarId && x.Id == id).ExecuteDeleteAsync();
        if (deleted == 0) throw new NotFoundException("Override not found");
    }

    private static void Apply(AvailabilityOverride o, SaveAvailabilityOverrideRequest req)
    {
        o.Date = req.Date ?? throw Invalid("date", "Required (YYYY-MM-DD)");
        o.IsUnavailable = req.IsUnavailable;
        o.Reason = OptionalText(req.Reason, "reason", 500);
        if (req.IsUnavailable)
        {
            o.StartTime = null;
            o.EndTime = null;
            return;
        }
        if (!TryParseTime(req.StartTime, false, out var start)) throw Invalid("start_time", "Required (HH:mm) unless is_unavailable");
        if (!TryParseTime(req.EndTime, true, out var end)) throw Invalid("end_time", "Required (HH:mm) unless is_unavailable");
        if (start.ToTimeSpan() >= SlotEngine.EndOffset(end)) throw Invalid("end_time", "Must be after start_time");
        o.StartTime = start;
        o.EndTime = end;
    }

    // ---- Appointment types ----

    public Task<List<AppointmentType>> ListTypesAsync(Guid appId, Guid? calendarId)
    {
        var query = db.AppointmentTypes.Where(t => t.AppId == appId);
        if (calendarId is { } c) query = query.Where(t => t.CalendarId == c);
        return query.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<AppointmentType> GetTypeAsync(Guid appId, Guid id)
        => await db.AppointmentTypes.Include(t => t.Calendar).FirstOrDefaultAsync(t => t.AppId == appId && t.Id == id)
           ?? throw new NotFoundException("Appointment type not found");

    public async Task<AppointmentType> CreateTypeAsync(Guid appId, SaveAppointmentTypeRequest req)
    {
        var type = new AppointmentType { AppId = appId };
        await ApplyAsync(type, req, creating: true);
        db.AppointmentTypes.Add(type);
        await SaveUniqueAsync("An appointment type with this slug already exists");
        return type;
    }

    public async Task<AppointmentType> UpdateTypeAsync(Guid appId, Guid id, SaveAppointmentTypeRequest req)
    {
        var type = await GetTypeAsync(appId, id);
        await ApplyAsync(type, req, creating: false);
        await SaveUniqueAsync("An appointment type with this slug already exists");
        return type;
    }

    public async Task DeleteTypeAsync(Guid appId, Guid id)
    {
        var type = await GetTypeAsync(appId, id);
        if (await db.Appointments.AnyAsync(a => a.AppId == appId && a.AppointmentTypeId == id))
            throw new ConflictException("This appointment type has appointments; deactivate it instead");
        db.AppointmentTypes.Remove(type);
        await db.SaveChangesAsync();
    }

    private async Task ApplyAsync(AppointmentType t, SaveAppointmentTypeRequest req, bool creating)
    {
        if (creating || req.CalendarId is not null)
        {
            var calendarId = req.CalendarId ?? throw Invalid("calendar_id", "Required");
            if (!await db.BookingCalendars.AnyAsync(c => c.AppId == t.AppId && c.Id == calendarId))
                throw Invalid("calendar_id", "Calendar not found");
            t.CalendarId = calendarId;
        }
        if (creating || req.Name is not null) t.Name = RequireText(req.Name, "name", 200);
        if (creating || req.Slug is not null)
        {
            Slugs.EnsureValid(req.Slug);
            t.Slug = req.Slug!;
        }
        if (req.Description is not null) t.Description = OptionalText(req.Description, "description", 5000, singleLine: false);
        if (creating || req.DurationMinutes is not null)
            t.DurationMinutes = Range(req.DurationMinutes, "duration_minutes", 5, 1440);
        if (req.BufferBeforeMinutes is not null) t.BufferBeforeMinutes = Range(req.BufferBeforeMinutes, "buffer_before_minutes", 0, 1440);
        if (req.BufferAfterMinutes is not null) t.BufferAfterMinutes = Range(req.BufferAfterMinutes, "buffer_after_minutes", 0, 1440);
        if (req.SlotIntervalMinutes is not null) t.SlotIntervalMinutes = Range(req.SlotIntervalMinutes, "slot_interval_minutes", 5, 1440);
        else if (creating) t.SlotIntervalMinutes = t.DurationMinutes;
        if (req.MinNoticeMinutes is not null) t.MinNoticeMinutes = Range(req.MinNoticeMinutes, "min_notice_minutes", 0, 525600);
        if (req.MaxAdvanceDays is not null) t.MaxAdvanceDays = Range(req.MaxAdvanceDays, "max_advance_days", 1, 730);
        if (req.MaxPerDay is not null) t.MaxPerDay = req.MaxPerDay == 0 ? null : Range(req.MaxPerDay, "max_per_day", 1, 1000);
        if (req.RequiresApproval is { } approval) t.RequiresApproval = approval;
        if (req.IntakeSchema is { } schema) t.IntakeSchema = ValidateSchema(schema);
        if (req.Location is not null) t.Location = OptionalText(req.Location, "location", 500);
        if (req.MeetingUrl is not null) t.MeetingUrl = OptionalUrl(req.MeetingUrl, "meeting_url", requireToken: false);
        if (req.ManageUrlTemplate is not null) t.ManageUrlTemplate = OptionalUrl(req.ManageUrlTemplate, "manage_url_template", requireToken: true);
        if (req.IsActive is { } active) t.IsActive = active;
    }

    private static int Range(int? value, string pointer, int min, int max)
        => value is { } v && v >= min && v <= max ? v : throw Invalid(pointer, $"Must be between {min} and {max}");

    /// <summary>null / JSON null clears; otherwise must be an object that compiles as a JSON Schema.</summary>
    private static string? ValidateSchema(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Null) return null;
        if (schema.ValueKind != JsonValueKind.Object) throw Invalid("intake_schema", "Must be a JSON Schema object");
        var text = schema.GetRawText();
        try
        {
            JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        }
        catch (Exception) // JsonSchema.Net throws a variety of types for malformed schemas
        {
            throw Invalid("intake_schema", "Not a valid JSON Schema");
        }
        return text;
    }

    private static string? OptionalUrl(string value, string pointer, bool requireToken)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 2000) throw Invalid(pointer, "At most 2000 characters");
        if (requireToken && !trimmed.Contains("{token}")) throw Invalid(pointer, "Must contain {token}");
        var probe = trimmed.Replace("{token}", "token");
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri)
            || (requireToken ? uri.Scheme != Uri.UriSchemeHttps : uri.Scheme is not ("http" or "https")))
            throw Invalid(pointer, requireToken ? "Must be an absolute https URL" : "Must be an absolute http(s) URL");
        return trimmed;
    }

    private async Task SaveUniqueAsync(string conflictMessage)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }
}
