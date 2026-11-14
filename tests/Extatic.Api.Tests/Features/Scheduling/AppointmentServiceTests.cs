using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Features.Scheduling;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Extatic.Api.Tests.Features.Scheduling;

public class AppointmentServiceTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero); // Sunday
    private static readonly DateTime Mon0900 = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mon1000 = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly MonDate = new(2026, 3, 2);

    private SchedulingTestKit Kit() => new(fixture, Now);

    private static async Task<List<DateTime>> SlotStarts(AppointmentService svc, AppointmentType type, DateOnly from, DateOnly to, string? tz = null)
        => (await svc.GetSlotsAsync(type, from, to, tz)).Days.SelectMany(d => d.Slots).Select(s => s.StartsAt).ToList();

    [Fact]
    public async Task Book_ConfirmsAndHidesTheSlot_StoresOnlyTokenHash_NormalisesEmail()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (db, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        var (appointment, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal("customer@example.com", appointment.CustomerEmail);
        await using var check = fixture.CreateContext();
        var saved = await check.Appointments.SingleAsync(a => a.Id == appointment.Id);
        Assert.Equal(Tokens.Hash(token), saved.ManageTokenHash);
        Assert.DoesNotContain(token, saved.ManageTokenHash);
        Assert.Equal(Mon0900, saved.StartsAt);
        Assert.Equal(Mon0900.AddMinutes(30), saved.EndsAt);
        Assert.DoesNotContain(Mon0900, await SlotStarts(svc, type, MonDate, MonDate.AddDays(1)));
    }

    [Fact]
    public async Task Book_RejectsTimesThatAreNotGeneratedSlots()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddMinutes(10)), null));
        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddHours(-2)), null));
        // Within min notice (now + 120 min).
        kit.Time.Now = new DateTimeOffset(Mon0900.AddMinutes(-60));
        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null));
    }

    [Fact]
    public async Task ConcurrentBookingsOfTheSameSlot_ExactlyOneSucceeds()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        const int n = 12;
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            await start.Task;
            var (db, svc, config) = kit.Services();
            await using (db)
            {
                var type = await config.GetTypeAsync(app.Id, seeded.Id);
                try
                {
                    await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, $"c{i}@example.com"), null);
                    return true;
                }
                catch (ConflictException) { return false; }
            }
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.Appointments.CountAsync(a => a.AppId == app.Id));
        // Exactly one booking's emails and webhooks were staged (losers rolled back atomically).
        Assert.Equal(1, await check.EmailOutbox.CountAsync(m => m.AppId == app.Id && m.Category == "scheduling.booked"));
        Assert.Equal(1, await check.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id && l.EventType == SchedulingEvents.Booked));
    }

    [Fact]
    public async Task ConcurrentBookingsOfDifferentOverlappingTypes_ExactlyOneSucceeds()
    {
        var kit = Kit();
        var (app, calendar, seeded) = await kit.SeedAsync();
        var longType = await kit.AddTypeAsync(app, calendar, "long", t => { t.DurationMinutes = 60; t.SlotIntervalMinutes = 30; });
        const int n = 10;
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            await start.Task;
            var (db, svc, config) = kit.Services();
            await using (db)
            {
                // Alternate: 30-min at 09:30 vs 60-min at 09:00 (overlapping blocks on one calendar).
                var type = await config.GetTypeAsync(app.Id, i % 2 == 0 ? seeded.Id : longType.Id);
                var at = i % 2 == 0 ? Mon0900.AddMinutes(30) : Mon0900;
                try { await svc.BookAsync(type, SchedulingTestKit.Input(at, $"c{i}@example.com"), null); return true; }
                catch (ConflictException) { return false; }
            }
        }).ToList();
        start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(tasks)).Count(r => r));
    }

    [Fact]
    public async Task ExclusionConstraint_RejectsOverlappingActiveRows_AtTheDatabaseLevel()
    {
        var kit = Kit();
        var (app, calendar, type) = await kit.SeedAsync();
        Extatic.Api.Features.Scheduling.Appointment Row(DateTime s, AppointmentStatus status) => new()
        {
            AppId = app.Id, CalendarId = calendar.Id, AppointmentTypeId = type.Id, CustomerName = "x", CustomerEmail = "x@example.com",
            StartsAt = s, EndsAt = s.AddMinutes(30), BlockStartsAt = s, BlockEndsAt = s.AddMinutes(30),
            Status = status, ManageTokenHash = Tokens.Hash(Guid.NewGuid().ToString())
        };
        await using (var db = fixture.CreateContext())
        {
            db.Appointments.Add(Row(Mon0900, AppointmentStatus.Confirmed));
            db.Appointments.Add(Row(Mon0900, AppointmentStatus.Cancelled)); // cancelled rows never conflict
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext())
        {
            db.Appointments.Add(Row(Mon0900.AddMinutes(15), AppointmentStatus.Pending));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(SchedulingSchema.ExclusionViolationSqlState, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task SharedCalendar_BookingOneTypeBlocksOtherTypes()
    {
        var kit = Kit();
        var (app, calendar, seeded) = await kit.SeedAsync();
        var other = await kit.AddTypeAsync(app, calendar, "other", t => { t.DurationMinutes = 60; t.SlotIntervalMinutes = 30; });
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var otherType = await config.GetTypeAsync(app.Id, other.Id);

        await svc.BookAsync(type, SchedulingTestKit.Input(Mon1000), null);

        var otherSlots = await SlotStarts(svc, otherType, MonDate, MonDate.AddDays(1));
        Assert.DoesNotContain(Mon0900.AddMinutes(30), otherSlots); // 09:30–10:30 overlaps 10:00
        Assert.DoesNotContain(Mon1000, otherSlots);
        Assert.Contains(Mon0900, otherSlots);
        Assert.Contains(Mon1000.AddMinutes(30), otherSlots);
        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(otherType, SchedulingTestKit.Input(Mon0900.AddMinutes(30)), null));
    }

    [Fact]
    public async Task MaxPerDay_HoldsUnderConcurrentBookingsOfDifferentSlots()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync(configure: t => t.MaxPerDay = 2);
        const int n = 8;
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            await start.Task;
            var (db, svc, config) = kit.Services();
            await using (db)
            {
                var type = await config.GetTypeAsync(app.Id, seeded.Id);
                try { await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddMinutes(30 * i), $"c{i}@example.com"), null); return true; }
                catch (ConflictException) { return false; }
            }
        }).ToList();
        start.SetResult();
        Assert.Equal(2, (await Task.WhenAll(tasks)).Count(r => r));
        var (_, check, checkConfig) = kit.Services();
        Assert.DoesNotContain(await SlotStarts(check, await checkConfig.GetTypeAsync(app.Id, seeded.Id), MonDate, MonDate.AddDays(1)),
            s => s.Date == MonDate.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public async Task Slots_GroupedByRequestedTimezone_WithUtcTimes()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync("Europe/Berlin");
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        var result = await svc.GetSlotsAsync(type, MonDate, MonDate.AddDays(2), "Asia/Tokyo");

        Assert.Equal("Asia/Tokyo", result.Timezone);
        // Berlin 09:00–17:00 CET = 08:00–16:00Z = 17:00–01:00 JST, so Monday's last two slots fall on Tuesday in Tokyo.
        var monday = result.Days.Single(d => d.Date == MonDate);
        Assert.Equal(14, monday.Slots.Count);
        Assert.Equal(new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc), monday.Slots[0].StartsAt);
        Assert.Equal("17:00", monday.Slots[0].LocalTime);
        var tuesday = result.Days.Single(d => d.Date == MonDate.AddDays(1));
        Assert.Equal("00:00", tuesday.Slots[0].LocalTime);
        Assert.Equal(16, tuesday.Slots.Count);

        var defaultZone = await svc.GetSlotsAsync(type, MonDate, MonDate.AddDays(1), null);
        Assert.Equal("Europe/Berlin", defaultZone.Timezone);
        Assert.Equal("09:00", defaultZone.Days[0].Slots[0].LocalTime);

        await Assert.ThrowsAsync<BadRequestException>(() => svc.GetSlotsAsync(type, MonDate, MonDate.AddDays(63), null));
        await Assert.ThrowsAsync<ValidationException>(() => svc.GetSlotsAsync(type, MonDate, MonDate.AddDays(1), "Nope/Zone"));
    }

    [Fact]
    public async Task Cancel_FreesTheSlot_AndStagesEmailsAndWebhook()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (booked, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);

        var cancelled = await svc.CancelByCustomerAsync(app.Id, token, "Can't make it");

        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Contains(Mon0900, await SlotStarts(svc, type, MonDate, MonDate.AddDays(1)));
        await using var check = fixture.CreateContext();
        Assert.True(await check.EmailOutbox.AnyAsync(m => m.AppId == app.Id && m.Category == "scheduling.cancelled" && m.ToAddress == "customer@example.com"));
        Assert.True(await check.EmailOutbox.AnyAsync(m => m.AppId == app.Id && m.Category == "scheduling.staff" && m.ToAddress == "staff@example.com" && m.Subject.Contains("cancelled")));
        var cancelLog = await check.WebhookDeliveryLogs.SingleAsync(l => l.Webhook.AppId == app.Id && l.EventType == SchedulingEvents.Cancelled);
        Assert.Contains(booked.Id.ToString(), cancelLog.Payload);
        // Rebooking the freed slot works; a second cancel is a conflict.
        await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, "next@example.com"), null);
        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelByCustomerAsync(app.Id, token, null));
    }

    [Fact]
    public async Task CustomerCancelAndReschedule_RespectMinNotice_AdminBypasses()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (booked, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon1000), null);

        kit.Time.Now = new DateTimeOffset(Mon1000.AddMinutes(-90)); // inside the 120-minute notice
        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelByCustomerAsync(app.Id, token, null));
        await Assert.ThrowsAsync<ConflictException>(() => svc.RescheduleByCustomerAsync(app.Id, token, Mon1000.AddHours(4)));
        var public_ = (await svc.GetByTokenAsync(app.Id, token)).ToPublicDto(kit.Now);
        Assert.False(public_.CanCancel);

        // Admin may move it to a short-notice slot (bypassing min notice) and cancel.
        var moved = await svc.RescheduleByAdminAsync(app.Id, booked.Id, Mon1000.AddMinutes(30));
        Assert.Equal(Mon1000.AddMinutes(30), moved.StartsAt);
        var cancelled = await svc.CancelByAdminAsync(app.Id, booked.Id, "Clinic closed");
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Reschedule_IsAtomic_OldSlotFreedNewTaken_FailureLeavesOriginal()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync(configure: t => { t.BufferAfterMinutes = 30; });
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (a, tokenA) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        var (b, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddHours(3), "b@example.com"), null);

        // Moving A by 30 minutes overlaps its own old block only — allowed (self excluded).
        var moved = await svc.RescheduleByCustomerAsync(app.Id, tokenA, Mon0900.AddMinutes(30));
        Assert.Equal(Mon0900.AddMinutes(30), moved.StartsAt);
        Assert.Equal(Mon0900.AddMinutes(90), moved.BlockEndsAt);
        var slots = await SlotStarts(svc, type, MonDate, MonDate.AddDays(1));
        Assert.DoesNotContain(Mon0900.AddMinutes(30), slots);
        Assert.Contains(Mon0900.AddMinutes(90), slots); // adjacent to the new block (09:30–10:30 incl. buffer)

        // Moving A onto B fails and leaves A unchanged.
        await Assert.ThrowsAsync<ConflictException>(() => svc.RescheduleByCustomerAsync(app.Id, tokenA, b.StartsAt));
        await using (var check = fixture.CreateContext())
        {
            var row = await check.Appointments.SingleAsync(x => x.Id == a.Id);
            Assert.Equal(Mon0900.AddMinutes(30), row.StartsAt);
            Assert.Equal(AppointmentStatus.Confirmed, row.Status);
            Assert.Equal(1, await check.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id && l.EventType == SchedulingEvents.Rescheduled));
        }
    }

    [Fact]
    public async Task Reschedule_DatabaseConstraintRejectsARacingMove_OriginalIntact()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (a, tokenA) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        var (_, tokenB) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddHours(1), "b@example.com"), null);

        // Both race for 13:00.
        var target = Mon0900.AddHours(4);
        var start = new TaskCompletionSource();
        async Task<bool> Move(string token)
        {
            await start.Task;
            var (db, s, _) = kit.Services();
            await using (db)
            {
                try { await s.RescheduleByCustomerAsync(app.Id, token, target); return true; }
                catch (ConflictException) { return false; }
            }
        }
        var t1 = Move(tokenA);
        var t2 = Move(tokenB);
        start.SetResult();
        var results = await Task.WhenAll(t1, t2);
        Assert.Equal(1, results.Count(r => r));
        await using var check = fixture.CreateContext();
        var starts = await check.Appointments.Where(x => x.AppId == app.Id).Select(x => x.StartsAt).ToListAsync();
        Assert.Single(starts, s => s == target);
        Assert.Single(starts, s => s == Mon0900 || s == Mon0900.AddHours(1));
    }

    [Fact]
    public async Task ApprovalFlow_PendingBlocksSlot_ConfirmOrDecline()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync(configure: t => t.RequiresApproval = true);
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        var (pending, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        Assert.Equal(AppointmentStatus.Pending, pending.Status);
        Assert.Null((await svc.GetByTokenAsync(app.Id, token)).ToPublicDto(kit.Now).MeetingUrl);
        Assert.DoesNotContain(Mon0900, await SlotStarts(svc, type, MonDate, MonDate.AddDays(1)));
        await Assert.ThrowsAsync<ConflictException>(() => svc.CompleteAsync(app.Id, pending.Id));

        var confirmed = await svc.ConfirmAsync(app.Id, pending.Id);
        Assert.Equal(AppointmentStatus.Confirmed, confirmed.Status);
        Assert.Equal("https://meet.example.com/x", (await svc.GetByTokenAsync(app.Id, token)).ToPublicDto(kit.Now).MeetingUrl);
        await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmAsync(app.Id, pending.Id));

        var (second, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon1000, "two@example.com"), null);
        var declined = await svc.DeclineAsync(app.Id, second.Id, "Fully booked");
        Assert.Equal(AppointmentStatus.Cancelled, declined.Status);
        Assert.Contains(Mon1000, await SlotStarts(svc, type, MonDate, MonDate.AddDays(1)));

        await using var check = fixture.CreateContext();
        var categories = await check.EmailOutbox.Where(m => m.AppId == app.Id).Select(m => m.Category).ToListAsync();
        Assert.Contains("scheduling.approved", categories);
        Assert.Contains("scheduling.declined", categories);
        var pendingEmail = await check.EmailOutbox.FirstAsync(m => m.AppId == app.Id && m.ToAddress == "customer@example.com" && m.Category == "scheduling.booked");
        Assert.StartsWith("Booking request received", pendingEmail.Subject);
        var events = await check.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == app.Id).Select(l => l.EventType).ToListAsync();
        Assert.Equal(2, events.Count(e => e == SchedulingEvents.Booked));
        Assert.Equal(1, events.Count(e => e == SchedulingEvents.Confirmed));
        Assert.Equal(1, events.Count(e => e == SchedulingEvents.Cancelled));

        var completed = await svc.CompleteAsync(app.Id, pending.Id);
        Assert.Equal(AppointmentStatus.Completed, completed.Status);
        Assert.True(await check.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id && l.EventType == SchedulingEvents.Completed));
    }

    [Fact]
    public async Task IntakeSchema_InvalidAnswersReturn422_AndNothingIsWritten()
    {
        var kit = Kit();
        var schema = """{"type":"object","required":["reason"],"properties":{"reason":{"type":"string","minLength":3}}}""";
        var (app, _, seeded) = await kit.SeedAsync(configure: t => t.IntakeSchema = schema);
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, data: new { reason = "x" }), null));
        Assert.NotEmpty(ex.Errors);
        await Assert.ThrowsAsync<ValidationException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, data: new { }), null));
        await using (var check = fixture.CreateContext())
            Assert.False(await check.Appointments.AnyAsync(a => a.AppId == app.Id));

        var (ok, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, data: new { reason = "Check-up" }), null);
        Assert.Contains("Check-up", ok.Data);
    }

    [Fact]
    public async Task Token_CannotCrossTenants()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (otherApp, _, _) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (_, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetByTokenAsync(otherApp.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.CancelByCustomerAsync(otherApp.Id, token, null));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RescheduleByCustomerAsync(otherApp.Id, token, Mon1000));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetByTokenAsync(app.Id, token + "x"));
        Assert.Equal(AppointmentStatus.Confirmed, (await svc.GetByTokenAsync(app.Id, token)).Status);
    }

    [Fact]
    public async Task PlatformOperations_AreTenantScoped()
    {
        var kit = Kit();
        var (app, calendar, seeded) = await kit.SeedAsync();
        var (otherApp, _, _) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (booked, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);

        await Assert.ThrowsAsync<NotFoundException>(() => config.GetCalendarAsync(otherApp.Id, calendar.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => config.GetTypeAsync(otherApp.Id, type.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => config.ReplaceRulesAsync(otherApp.Id, calendar.Id, new([])));
        await Assert.ThrowsAsync<NotFoundException>(() => config.ListOverridesAsync(otherApp.Id, calendar.Id, null, null));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(otherApp.Id, booked.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.CancelByAdminAsync(otherApp.Id, booked.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmAsync(otherApp.Id, booked.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RescheduleByAdminAsync(otherApp.Id, booked.Id, Mon1000));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateNotesAsync(otherApp.Id, booked.Id, "x"));
        Assert.Equal(0, (await svc.ListAsync(otherApp.Id, new(null, null, null, null, null), 1, 50)).Total);
        Assert.Equal(1, (await svc.ListAsync(app.Id, new(null, null, null, null, null), 1, 50)).Total);
        // A type in another App cannot be attached to this App's calendar, nor vice versa.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(otherApp.Id,
            new SaveAppointmentTypeRequest(calendar.Id, "X", "x", null, 30, null, null, null, null, null, null, null, null, null, null, null, null)));
        Assert.Equal("calendar_id", ex.Errors.Single().Pointer);
    }

    [Fact]
    public async Task ReminderWorker_StagesExactlyOnce_AcrossConcurrentReplicas()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (booked, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        Assert.Null(booked.ReminderSentAt); // booked > 24 h ahead

        var workers = Enumerable.Range(0, 6).Select(_ => new AppointmentReminderWorker(kit.ScopeFactory(), kit.Time, NullLogger<AppointmentReminderWorker>.Instance)).ToList();
        // The fixture database is shared by this class's tests, so count this App's reminders only.
        async Task<int> Reminders()
        {
            await using var db = fixture.CreateContext();
            return await db.EmailOutbox.CountAsync(m => m.AppId == app.Id && m.Category == "scheduling.reminder");
        }
        // Not yet due.
        await workers[0].ProcessDueAsync();
        Assert.Equal(0, await Reminders());

        kit.Time.Now = new DateTimeOffset(Mon0900.AddHours(-23));
        await Task.WhenAll(workers.Select(w => w.ProcessDueAsync()));
        Assert.Equal(1, await Reminders());
        await Task.WhenAll(workers.Select(w => w.ProcessDueAsync()));
        Assert.Equal(1, await Reminders());

        await using var check = fixture.CreateContext();
        var reminders = await check.EmailOutbox.Where(m => m.AppId == app.Id && m.Category == "scheduling.reminder").ToListAsync();
        var reminder = Assert.Single(reminders);
        Assert.Contains("(UTC)", reminder.TextBody);
        Assert.NotNull((await check.Appointments.SingleAsync(a => a.Id == booked.Id)).ReminderSentAt);
    }

    [Fact]
    public async Task ReminderWorker_SkipsCancelledAndShortNoticeBookings_RescheduleResets()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (cancelled, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        await svc.CancelByAdminAsync(app.Id, cancelled.Id, null);

        kit.Time.Now = new DateTimeOffset(Mon0900.AddHours(-20));
        var (soon, tokenSoon) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon1000), null);
        Assert.NotNull(soon.ReminderSentAt); // the confirmation email is the reminder

        var worker = new AppointmentReminderWorker(kit.ScopeFactory(), kit.Time, NullLogger<AppointmentReminderWorker>.Instance);
        async Task<int> Reminders()
        {
            await using var db = fixture.CreateContext();
            return await db.EmailOutbox.CountAsync(m => m.AppId == app.Id && m.Category == "scheduling.reminder");
        }
        await worker.ProcessDueAsync();
        Assert.Equal(0, await Reminders());

        // Rescheduled > 24 h out: reminder baseline reset, due later.
        var later = new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        var moved = await svc.RescheduleByCustomerAsync(app.Id, tokenSoon, later);
        Assert.Null(moved.ReminderSentAt);
        kit.Time.Now = new DateTimeOffset(later.AddHours(-2));
        await worker.ProcessDueAsync();
        Assert.Equal(1, await Reminders());
    }

    [Fact]
    public async Task Emails_UseCustomerTimezone_NameTheZone_AndEncodeHtml()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync("Europe/Berlin");
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var input = SchedulingTestKit.Input(new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc), timezone: "America/New_York")
            with { Name = "<script>alert(1)</script>" };

        var (_, token) = await svc.BookAsync(type, input, null);

        await using var check = fixture.CreateContext();
        var customer = await check.EmailOutbox.SingleAsync(m => m.AppId == app.Id && m.Category == "scheduling.booked");
        Assert.Contains("Monday, 2 March 2026, 03:00–03:30 (America/New_York)", customer.TextBody);
        Assert.Contains(Uri.EscapeDataString(token), customer.TextBody);
        Assert.DoesNotContain("<script>", customer.HtmlBody);
        Assert.Contains("&lt;script&gt;", customer.HtmlBody);
        var staff = await check.EmailOutbox.SingleAsync(m => m.AppId == app.Id && m.Category == "scheduling.staff");
        Assert.Contains("09:00–09:30 (Europe/Berlin)", staff.TextBody);
        Assert.Equal("customer@example.com", staff.ReplyTo);
    }

    [Fact]
    public async Task AdminManualBooking_BypassesMinNotice_ButNeverOverlap()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        kit.Time.Now = new DateTimeOffset(Mon0900.AddMinutes(-10));

        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null));
        var (manual, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null, byAdmin: true, notes: "Walk-in");
        Assert.Equal(AppointmentStatus.Confirmed, manual.Status);
        Assert.Equal("Walk-in", manual.Notes);
        await Assert.ThrowsAsync<ConflictException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, "x@example.com"), null, byAdmin: true));
    }

    [Fact]
    public async Task Ics_UsesUtc_EscapesText_FoldsLines()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync(configure: t =>
        {
            t.Name = "Consult; with, commas\\and a very long name that will certainly need folding — ünïcödé";
            t.Location = "Room 1, Main St";
        });
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var (_, token) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        var appointment = await svc.GetByTokenAsync(app.Id, token);

        var ics = IcsBuilder.Build(appointment, kit.Now);

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.Contains("METHOD:PUBLISH\r\n", ics);
        Assert.Contains("DTSTART:20260302T090000Z\r\n", ics);
        Assert.Contains("DTEND:20260302T093000Z\r\n", ics);
        Assert.Contains("LOCATION:Room 1\\, Main St\r\n", ics);
        Assert.Contains("STATUS:CONFIRMED", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        var physical = ics.Split("\r\n");
        Assert.All(physical, l => Assert.True(System.Text.Encoding.UTF8.GetByteCount(l) <= 75, l));
        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("SUMMARY:Consult\\; with\\, commas\\\\and a very long name", unfolded);
        Assert.Contains("ünïcödé", unfolded);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));
    }

    [Fact]
    public async Task CalendarAndTypeDeletion_BlockedWhileAppointmentsExist()
    {
        var kit = Kit();
        var (app, calendar, seeded) = await kit.SeedAsync();
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);

        await Assert.ThrowsAsync<ConflictException>(() => config.DeleteTypeAsync(app.Id, type.Id));
        await Assert.ThrowsAsync<ConflictException>(() => config.DeleteCalendarAsync(app.Id, calendar.Id));

        // Deleting the App cascades through everything.
        await using var db = fixture.CreateContext();
        await db.Apps.Where(a => a.Id == app.Id).ExecuteDeleteAsync();
        Assert.False(await db.Appointments.AnyAsync(a => a.AppId == app.Id));
        Assert.False(await db.BookingCalendars.AnyAsync(c => c.AppId == app.Id));
    }

    [Fact]
    public async Task ExportCsv_ContainsLocalTimeAndPreventsFormulaInjection()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync("Europe/Berlin");
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        await svc.BookAsync(type, SchedulingTestKit.Input(new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc)) with { Name = "=HYPERLINK(1)" }, null);

        var csv = System.Text.Encoding.UTF8.GetString(await svc.ExportCsvAsync(app.Id, new(null, null, null, null, null)));
        Assert.Contains("2026-03-02 09:00", csv);
        Assert.Contains("Europe/Berlin", csv);
        Assert.Contains("'=HYPERLINK(1)", csv);
    }

    [Fact]
    public async Task List_FiltersByRangeCalendarTypeAndStatus()
    {
        var kit = Kit();
        var (app, calendar, seeded) = await kit.SeedAsync();
        var other = await kit.AddTypeAsync(app, calendar, "other");
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);
        var otherType = await config.GetTypeAsync(app.Id, other.Id);
        var (a, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        await svc.BookAsync(otherType, SchedulingTestKit.Input(Mon1000), null);
        await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900.AddDays(1)), null);
        await svc.CancelByAdminAsync(app.Id, a.Id, null);

        Assert.Equal(2, (await svc.ListAsync(app.Id, new(Mon0900, Mon0900.AddHours(12), null, null, null), 1, 50)).Total);
        Assert.Equal(2, (await svc.ListAsync(app.Id, new(null, null, null, type.Id, null), 1, 50)).Total);
        Assert.Equal(1, (await svc.ListAsync(app.Id, new(null, null, null, null, "cancelled"), 1, 50)).Total);
        Assert.Equal(2, (await svc.ListAsync(app.Id, new(null, null, calendar.Id, null, "Confirmed,Pending"), 1, 50)).Total);
        var page = await svc.ListAsync(app.Id, new(null, null, null, null, null), 2, 2);
        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
        await Assert.ThrowsAsync<BadRequestException>(() => svc.ListAsync(app.Id, new(null, null, null, null, "bogus"), 1, 50));
    }

    [Fact]
    public async Task Booking_StagesEmailsAndWebhooksInTheSameTransaction()
    {
        var kit = Kit();
        var (app, _, seeded) = await kit.SeedAsync(notifyEmails: ["a@example.com", "b@example.com"]);
        var (_, svc, config) = kit.Services();
        var type = await config.GetTypeAsync(app.Id, seeded.Id);

        // An invalid customer email aborts before anything is staged or written.
        await Assert.ThrowsAsync<ValidationException>(() => svc.BookAsync(type, SchedulingTestKit.Input(Mon0900, "not-an-email"), null));
        await using (var check = fixture.CreateContext())
        {
            Assert.False(await check.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
            Assert.False(await check.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id));
        }

        var (booked, _) = await svc.BookAsync(type, SchedulingTestKit.Input(Mon0900), null);
        await using var verify = fixture.CreateContext();
        Assert.Equal(3, await verify.EmailOutbox.CountAsync(m => m.AppId == app.Id)); // customer + 2 staff
        var logs = await verify.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == app.Id).ToListAsync();
        Assert.Equal(new[] { SchedulingEvents.Booked, SchedulingEvents.Confirmed }, logs.Select(l => l.EventType).Order());
        using var payload = JsonDocument.Parse(logs[0].Payload);
        Assert.Equal(booked.Id.ToString(), payload.RootElement.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal("customer@example.com", payload.RootElement.GetProperty("data").GetProperty("customer_email").GetString());
    }
}
