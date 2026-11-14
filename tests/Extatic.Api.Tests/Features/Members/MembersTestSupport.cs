using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Members;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Members;

/// <summary>Controllable clock for expiry/reminder tests.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

public sealed class MembersHarness(ApiTestFixture fixture, TestClock? clock = null)
{
    public TestClock Clock { get; } = clock ?? new TestClock(DateTimeOffset.UtcNow);
    public ApiTestFixture Fixture => fixture;

    public MembershipService Service(AppDbContext db)
        => new(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture), TestServices.EmailQueue(), Clock);

    public MembershipMaintenance Maintenance(AppDbContext db)
        => new(db, TestServices.WebhookDispatcher(fixture), TestServices.EmailQueue(), Clock);

    /// <summary>Runs <paramref name="action"/> with a fresh context + service.</summary>
    public async Task<T> With<T>(Func<MembershipService, Task<T>> action)
    {
        await using var db = fixture.CreateContext();
        return await action(Service(db));
    }

    public async Task With(Func<MembershipService, Task> action)
    {
        await using var db = fixture.CreateContext();
        await action(Service(db));
    }

    public Task<MembershipPlan> CreatePlanAsync(Guid appId, string slug = "standard", int? durationDays = 365,
        bool requiresApproval = false, int? maxMembers = null, bool selfSignup = true, bool isActive = true)
        => With(s => s.CreatePlanAsync(appId, new MembershipPlanRequest(
            $"Plan {slug}", slug, "A plan", "$40 / year", durationDays, requiresApproval, selfSignup, maxMembers,
            ["Newsletter", "Discounts"], isActive, 0)));

    public Task<Membership> JoinAsync(Guid appId, Guid appUserId, string planSlug, object? profile = null)
        => With(s => s.JoinAsync(appId, appUserId, new JoinMembershipRequest(planSlug,
            profile is null ? null : JsonSerializer.SerializeToElement(profile))));

    public async Task<Membership> LoadAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await db.Memberships.AsNoTracking().Include(m => m.Plan).FirstAsync(m => m.Id == id);
    }

    public async Task<List<string>> EmailCategoriesAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        // Sorted: rows staged in one SaveChanges share a timestamp.
        return (await db.EmailOutbox.Where(e => e.AppId == appId).Select(e => e.Category).ToListAsync()).Order().ToList();
    }

    public async Task<List<string>> WebhookEventsAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        return (await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == appId).Select(l => l.EventType)
            .ToListAsync()).Order().ToList();
    }

    /// <summary>Subscribes a webhook to all member.* events.</summary>
    public async Task SubscribeAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        db.Webhooks.Add(new Webhook
        {
            AppId = appId,
            Url = "https://hooks.example.com/members",
            Secret = "s",
            Events = [.. new MembersModule().WebhookEvents]
        });
        await db.SaveChangesAsync();
    }

    public static string Json(object value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
}
