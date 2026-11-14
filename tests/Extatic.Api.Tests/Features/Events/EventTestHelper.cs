using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Events;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;

namespace Extatic.Api.Tests.Features.Events;

internal static class EventTestHelper
{
    public static EventService Service(ApiTestFixture fixture, AppDbContext db)
        => new(db, new JsonSchemaValidator(),
            new EventNotifications(TestServices.EmailQueue(), TestServices.WebhookDispatcher(fixture)));

    public static EventRequest Request(string slug = "meetup", int? capacity = 10, bool waitlist = false,
        int maxSeats = 1, Action<EventRequest>? configure = null)
    {
        var req = new EventRequest
        {
            Title = "Monthly Meetup",
            Slug = slug,
            Description = "Talks, pizza; and \"networking\"",
            Location = "Room 1, Main St",
            OnlineUrl = "https://meet.example.com/abc",
            StartsAt = DateTimeOffset.UtcNow.AddDays(7),
            EndsAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
            Timezone = "Europe/London",
            Capacity = capacity,
            WaitlistEnabled = waitlist,
            MaxSeatsPerRegistration = maxSeats,
            NotifyEmails = ["organiser@example.com"]
        };
        configure?.Invoke(req);
        return req;
    }

    /// <summary>Creates and publishes an event.</summary>
    public static async Task<Event> SeedEventAsync(ApiTestFixture fixture, App app, EventRequest? req = null,
        bool publish = true)
    {
        await using var db = fixture.CreateContext();
        var service = Service(fixture, db);
        var ev = await service.CreateAsync(app, req ?? Request());
        if (publish) ev = await service.PublishAsync(app.Id, ev.Id);
        return ev;
    }

    public static async Task<Webhook> SeedWebhookAsync(ApiTestFixture fixture, Guid appId)
    {
        await using var db = fixture.CreateContext();
        var hook = new Webhook
        {
            AppId = appId,
            Url = "https://hooks.example.com/x",
            Secret = "s",
            Events = [.. EventNotifications.WebhookEvents]
        };
        db.Webhooks.Add(hook);
        await db.SaveChangesAsync();
        return hook;
    }

    public static async Task<(EventRegistration Registration, string Token)> RegisterAsync(
        ApiTestFixture fixture, App app, string slug, string email, int seats = 1, object? data = null,
        Guid? appUserId = null)
    {
        await using var db = fixture.CreateContext();
        var (r, _, token) = await Service(fixture, db).RegisterAsync(app, slug, appUserId,
            new RegisterRequest("Person " + email, email, seats,
                data is null ? null : JsonSerializer.SerializeToElement(data)));
        return (r, token);
    }

    public static async Task<EventRegistration> ReloadAsync(ApiTestFixture fixture, Guid id)
    {
        await using var db = fixture.CreateContext();
        return await db.EventRegistrations.FindAsync(id) ?? throw new InvalidOperationException();
    }
}
