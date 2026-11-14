using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Scheduling;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Extatic.Api.Tests.Features.Scheduling;

public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Builds scheduling services against the fixture database with a controllable clock.</summary>
public sealed class SchedulingTestKit(ApiTestFixture fixture, DateTimeOffset now)
{
    public ManualTimeProvider Time { get; } = new(now);
    public DateTime Now => Time.Now.UtcDateTime;
    public ApiTestFixture Fixture => fixture;

    public SchedulingNotifier Notifier() => new(TestServices.EmailQueue(), TestServices.WebhookDispatcher(fixture));

    public (AppDbContext Db, AppointmentService Appointments, SchedulingConfigService Config) Services()
    {
        var db = fixture.CreateContext();
        return (db, new AppointmentService(db, new JsonSchemaValidator(), Notifier(), Time), new SchedulingConfigService(db));
    }

    public IServiceScopeFactory ScopeFactory()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => fixture.CreateContext())
            .AddSingleton(Notifier())
            .BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>Seeds an App with a calendar (Mon–Fri 09:00–17:00 local) and a 30-minute type.</summary>
    public async Task<(App App, BookingCalendar Calendar, AppointmentType Type)> SeedAsync(
        string timezone = "UTC", Action<AppointmentType>? configure = null, string[]? notifyEmails = null,
        bool withWebhook = true)
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var calendar = new BookingCalendar
        {
            AppId = app.Id, Name = "Dr. Smith", Slug = "dr-smith", Timezone = timezone,
            NotifyEmails = notifyEmails ?? ["staff@example.com"]
        };
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            calendar.Rules.Add(new AvailabilityRule { AppId = app.Id, DayOfWeek = day, StartTime = new(9, 0), EndTime = new(17, 0) });
        var type = new AppointmentType
        {
            AppId = app.Id, Calendar = calendar, Name = "Consultation", Slug = "consult",
            DurationMinutes = 30, SlotIntervalMinutes = 30, MinNoticeMinutes = 120, MaxAdvanceDays = 60,
            Location = "Room 1, Main St", MeetingUrl = "https://meet.example.com/x",
            ManageUrlTemplate = "https://site.example.com/booking?token={token}"
        };
        configure?.Invoke(type);
        db.AppointmentTypes.Add(type);
        if (withWebhook)
            db.Webhooks.Add(new Webhook { AppId = app.Id, Url = "https://hooks.example.com/x", Secret = "s", Events = [.. SchedulingEvents.All] });
        await db.SaveChangesAsync();
        return (app, calendar, type);
    }

    public async Task<AppointmentType> AddTypeAsync(App app, BookingCalendar calendar, string slug, Action<AppointmentType>? configure = null)
    {
        await using var db = fixture.CreateContext();
        var type = new AppointmentType
        {
            AppId = app.Id, CalendarId = calendar.Id, Name = slug, Slug = slug,
            DurationMinutes = 30, SlotIntervalMinutes = 30, MinNoticeMinutes = 120, MaxAdvanceDays = 60
        };
        configure?.Invoke(type);
        db.AppointmentTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    public static BookingInput Input(DateTime start, string email = "Customer@Example.com", object? data = null, string? timezone = null)
        => new(start, "Casey Customer", email, "+1 555 0100", timezone,
            System.Text.Json.JsonSerializer.SerializeToElement(data ?? new { }));
}
