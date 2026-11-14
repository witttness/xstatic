using System.Text.Json;
using Extatic.Api.Features.Events;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Events.EventTestHelper;

namespace Extatic.Api.Tests.Features.Events;

public class EventRegistrationTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task ConcurrentRegistrationsForLastSeat_ExactlyOneConfirmed()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 3));
        await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await RegisterAsync(fixture, app, ev.Slug, "b@example.com");

        const int n = 12;
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, n).Select(i => Task.Run(async () =>
        {
            start.Wait();
            try { await RegisterAsync(fixture, app, ev.Slug, $"racer{i}@example.com"); return "ok"; }
            catch (RegistrationClosedException ex) { return ex.Reason; }
        })).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(n - 1, results.Count(r => r == RegistrationClosedReasons.SoldOut));
        await using var db = fixture.CreateContext();
        var seats = await db.EventRegistrations
            .Where(r => r.EventId == ev.Id && r.Status == EventRegistrationStatus.Confirmed).SumAsync(r => r.Seats);
        Assert.Equal(3, seats);
    }

    [Fact]
    public async Task ConcurrentRegistrations_WithWaitlist_NeverExceedCapacity()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 5, waitlist: true, maxSeats: 2));
        var tasks = Enumerable.Range(0, 16).Select(i =>
            Task.Run(() => RegisterAsync(fixture, app, ev.Slug, $"p{i}@example.com", seats: 1 + i % 2))).ToList();
        await Task.WhenAll(tasks);

        await using var db = fixture.CreateContext();
        var regs = await db.EventRegistrations.Where(r => r.EventId == ev.Id).ToListAsync();
        Assert.Equal(16, regs.Count);
        Assert.True(regs.Where(r => r.Status == EventRegistrationStatus.Confirmed).Sum(r => r.Seats) <= 5);
        // Once anyone is waitlisted, nobody created later is confirmed (no queue jumping).
        var firstWaitlisted = regs.Where(r => r.Status == EventRegistrationStatus.Waitlisted).Min(r => r.CreatedAt);
        Assert.DoesNotContain(regs, r => r.Status == EventRegistrationStatus.Confirmed && r.CreatedAt > firstWaitlisted);
    }

    [Fact]
    public async Task ConcurrentDuplicateEmail_OnlyOneRegistration()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: null));
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            try { await RegisterAsync(fixture, app, ev.Slug, i % 2 == 0 ? "Same@Example.com" : "same@example.com"); return true; }
            catch (ConflictException) { return false; }
        })).ToList();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(x => x));
    }

    [Fact]
    public async Task WaitlistPromotion_StrictFifo_OnCancelAndCapacityIncrease()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SeedWebhookAsync(fixture, app.Id);
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 2, waitlist: true, maxSeats: 3));
        var (a, aToken) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        var (b, _) = await RegisterAsync(fixture, app, ev.Slug, "b@example.com");
        var (c, _) = await RegisterAsync(fixture, app, ev.Slug, "c@example.com", seats: 2);
        var (d, _) = await RegisterAsync(fixture, app, ev.Slug, "d@example.com");
        Assert.Equal(EventRegistrationStatus.Confirmed, a.Status);
        Assert.Equal(EventRegistrationStatus.Confirmed, b.Status);
        Assert.Equal(EventRegistrationStatus.Waitlisted, c.Status);
        Assert.Equal(EventRegistrationStatus.Waitlisted, d.Status);

        // A cancels: 1 seat free; C (head, 2 seats) doesn't fit, so D must NOT jump ahead.
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelByTokenAsync(app.Id, aToken);
        Assert.Equal(EventRegistrationStatus.Waitlisted, (await ReloadAsync(fixture, c.Id)).Status);
        Assert.Equal(EventRegistrationStatus.Waitlisted, (await ReloadAsync(fixture, d.Id)).Status);

        // B cancelled by an admin: 2 seats free → C promoted, D still waits.
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelRegistrationAsync(app.Id, ev.Id, b.Id);
        var cNow = await ReloadAsync(fixture, c.Id);
        Assert.Equal(EventRegistrationStatus.Confirmed, cNow.Status);
        Assert.NotNull(cNow.PromotedAt);
        Assert.Equal(EventRegistrationStatus.Waitlisted, (await ReloadAsync(fixture, d.Id)).Status);

        // Capacity raised to 3 → D promoted.
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).UpdateAsync(app, ev.Id, Request(capacity: 3, waitlist: true, maxSeats: 3));
        Assert.Equal(EventRegistrationStatus.Confirmed, (await ReloadAsync(fixture, d.Id)).Status);

        await using var verify = fixture.CreateContext();
        var promotedEmails = await verify.EmailOutbox.Where(m => m.AppId == app.Id && m.Category == "events.promoted")
            .Select(m => m.ToAddress).ToListAsync();
        Assert.Equal(["c@example.com", "d@example.com"], promotedEmails.Order());
        var promotedHooks = await verify.WebhookDeliveryLogs
            .CountAsync(l => l.Webhook.AppId == app.Id && l.EventType == "event.registration.promoted");
        Assert.Equal(2, promotedHooks);
        Assert.Equal(2, await verify.WebhookDeliveryLogs
            .CountAsync(l => l.Webhook.AppId == app.Id && l.EventType == "event.registration.cancelled"));
    }

    [Fact]
    public async Task CapacityCannotDropBelowConfirmedSeats()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 5, maxSeats: 3));
        await RegisterAsync(fixture, app, ev.Slug, "a@example.com", seats: 3);
        await using var db = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Service(fixture, db).UpdateAsync(app, ev.Id, Request(capacity: 2, maxSeats: 3)));
        Assert.Contains(ex.Errors, e => e.Pointer == "capacity");
    }

    [Fact]
    public async Task MultiSeatRegistrations_RespectCapacityAndMaxSeats()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 5, maxSeats: 3));
        var (first, _) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com", seats: 3);
        Assert.Equal(EventRegistrationStatus.Confirmed, first.Status);

        var soldOut = await Assert.ThrowsAsync<RegistrationClosedException>(() =>
            RegisterAsync(fixture, app, ev.Slug, "b@example.com", seats: 3));
        Assert.Equal(RegistrationClosedReasons.SoldOut, soldOut.Reason);
        var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
            RegisterAsync(fixture, app, ev.Slug, "c@example.com", seats: 4));
        Assert.Contains(tooMany.Errors, e => e.Pointer == "seats");

        var (second, _) = await RegisterAsync(fixture, app, ev.Slug, "b@example.com", seats: 2);
        Assert.Equal(EventRegistrationStatus.Confirmed, second.Status);
        await using var db = fixture.CreateContext();
        var stats = await Service(fixture, db).AvailabilityAsync(ev);
        Assert.Equal(5, stats.ConfirmedSeats);
    }

    [Fact]
    public async Task DuplicateEmail_Conflict_UntilCancelled()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app);
        var (_, token) = await RegisterAsync(fixture, app, ev.Slug, "dup@example.com");
        await Assert.ThrowsAsync<ConflictException>(() => RegisterAsync(fixture, app, ev.Slug, " DUP@example.com "));
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelByTokenAsync(app.Id, token);
        var (again, _) = await RegisterAsync(fixture, app, ev.Slug, "dup@example.com");
        Assert.Equal(EventRegistrationStatus.Confirmed, again.Status);
    }

    [Fact]
    public async Task ClosedRegistration_ClearErrors()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var draft = await SeedEventAsync(fixture, app, Request(slug: "draft"), publish: false);
        await Assert.ThrowsAsync<NotFoundException>(() => RegisterAsync(fixture, app, draft.Slug, "x@example.com"));

        async Task<string> Reason(string slug)
            => (await Assert.ThrowsAsync<RegistrationClosedException>(() =>
                RegisterAsync(fixture, app, slug, "x@example.com"))).Reason;

        await SeedEventAsync(fixture, app, Request(slug: "later", configure: r => r.RegistrationOpensAt = DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(RegistrationClosedReasons.NotYetOpen, await Reason("later"));

        await SeedEventAsync(fixture, app, Request(slug: "closed", configure: r =>
        {
            r.RegistrationOpensAt = DateTimeOffset.UtcNow.AddDays(-2);
            r.RegistrationClosesAt = DateTimeOffset.UtcNow.AddDays(-1);
        }));
        Assert.Equal(RegistrationClosedReasons.Closed, await Reason("closed"));

        await SeedEventAsync(fixture, app, Request(slug: "past", configure: r =>
        {
            r.StartsAt = DateTimeOffset.UtcNow.AddDays(-2);
            r.EndsAt = DateTimeOffset.UtcNow.AddDays(-1);
        }));
        Assert.Equal(RegistrationClosedReasons.Ended, await Reason("past"));

        var cancelled = await SeedEventAsync(fixture, app, Request(slug: "cancelled"));
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelEventAsync(app.Id, cancelled.Id);
        Assert.Equal(RegistrationClosedReasons.Cancelled, await Reason("cancelled"));
    }

    [Fact]
    public async Task RegistrationSchema_ValidatesData()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "diet" },
            properties = new { diet = new { type = "string", @enum = new[] { "veg", "any" } } }
        });
        var ev = await SeedEventAsync(fixture, app, Request(configure: r => r.RegistrationSchema = schema));
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            RegisterAsync(fixture, app, ev.Slug, "a@example.com", data: new { diet = "meat" }));
        Assert.All(ex.Errors, e => Assert.StartsWith("/data", e.Pointer));
        var (ok, _) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com", data: new { diet = "veg" });
        Assert.Contains("veg", ok.Data);
    }

    [Fact]
    public async Task RequireAppUser_AnonymousRejected_SignedInLinked()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var user = await fixture.SeedAppUserAsync(app.Id);
        var ev = await SeedEventAsync(fixture, app, Request(configure: r => r.RequireAppUser = true));
        await Assert.ThrowsAsync<UnauthorizedException>(() => RegisterAsync(fixture, app, ev.Slug, "a@example.com"));

        await using var db = fixture.CreateContext();
        var service = Service(fixture, db);
        var (r, _, _) = await service.RegisterAsync(app, ev.Slug, user.Id, new RegisterRequest("Me", null, 1, null));
        Assert.Equal(user.Id, r.AppUserId);
        Assert.Equal(user.Email, r.Email);
        var (mine, total) = await service.ListForAppUserAsync(app.Id, user.Id, 1, 20);
        Assert.Equal(1, total);
        Assert.Equal(r.Id, mine[0].Id);
    }

    [Fact]
    public async Task EmailsAndWebhooks_StagedWithRegistration()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SeedWebhookAsync(fixture, app.Id);
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 1, waitlist: true,
            configure: r => r.ManageUrlTemplate = "https://site.example.com/manage?t={token}"));
        var (_, token) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await RegisterAsync(fixture, app, ev.Slug, "b@example.com");

        await using var db = fixture.CreateContext();
        var mails = await db.EmailOutbox.Where(m => m.AppId == app.Id).ToListAsync();
        var confirmation = Assert.Single(mails, m => m.Category == "events.confirmation");
        Assert.Contains($"https://site.example.com/manage?t={token}", confirmation.TextBody);
        Assert.Single(mails, m => m.Category == "events.waitlisted" && m.ToAddress == "b@example.com");
        Assert.Equal(2, mails.Count(m => m.Category == "events.notification" && m.ToAddress == "organiser@example.com"));
        var hooks = await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == app.Id).Select(l => l.EventType).ToListAsync();
        Assert.Contains("event.registration.created", hooks);
        Assert.Contains("event.registration.waitlisted", hooks);
        Assert.DoesNotContain(token, string.Join("", await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == app.Id).Select(l => l.Payload).ToListAsync()));
    }

    [Fact]
    public async Task EmailStagingFailure_RollsBackRegistration()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SeedWebhookAsync(fixture, app.Id);
        var ev = await SeedEventAsync(fixture, app);
        // Corrupt the notify list behind the validator's back so staging the notice throws.
        await using (var db = fixture.CreateContext())
            await db.Events.Where(e => e.Id == ev.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NotifyEmails, new[] { "not-an-email" }));

        await Assert.ThrowsAsync<ValidationException>(() => RegisterAsync(fixture, app, ev.Slug, "a@example.com"));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.EventRegistrations.AnyAsync(r => r.EventId == ev.Id));
        Assert.False(await verify.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
        Assert.False(await verify.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task EventCancelled_EmailsActiveRegistrants()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SeedWebhookAsync(fixture, app.Id);
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 1, waitlist: true));
        var (_, aToken) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await RegisterAsync(fixture, app, ev.Slug, "b@example.com");
        await RegisterAsync(fixture, app, ev.Slug, "c@example.com");
        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelByTokenAsync(app.Id, aToken); // b promoted, c waits

        await using (var db = fixture.CreateContext())
            await Service(fixture, db).CancelEventAsync(app.Id, ev.Id);
        await using var verify = fixture.CreateContext();
        var to = await verify.EmailOutbox.Where(m => m.AppId == app.Id && m.Category == "events.event_cancelled")
            .Select(m => m.ToAddress).ToListAsync();
        Assert.Equal(["b@example.com", "c@example.com"], to.Order());
        Assert.Equal(1, await verify.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id && l.EventType == "event.cancelled"));
    }

    [Fact]
    public async Task ManualAdd_CanOverrideCapacity_AndPromoteSpecific()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 1, waitlist: true));
        await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await using var db = fixture.CreateContext();
        var service = Service(fixture, db);

        var (waitlisted, _) = await service.AddManualAsync(app.Id, ev.Id, new("W", "w@example.com", 1, null));
        Assert.Equal(EventRegistrationStatus.Waitlisted, waitlisted.Status);
        var (vip, token) = await service.AddManualAsync(app.Id, ev.Id, new("VIP", "vip@example.com", 1, null, OverrideCapacity: true));
        Assert.Equal(EventRegistrationStatus.Confirmed, vip.Status);
        Assert.False(string.IsNullOrEmpty(token));

        await Assert.ThrowsAsync<ConflictException>(() => service.PromoteAsync(app.Id, ev.Id, waitlisted.Id, false));
        var promoted = await service.PromoteAsync(app.Id, ev.Id, waitlisted.Id, true);
        Assert.Equal(EventRegistrationStatus.Confirmed, promoted.Status);

        var checkedIn = await service.SetCheckInAsync(app.Id, ev.Id, promoted.Id, true);
        Assert.NotNull(checkedIn.CheckedInAt);
        Assert.Null((await service.SetCheckInAsync(app.Id, ev.Id, promoted.Id, false)).CheckedInAt);
    }

    [Fact]
    public async Task ManageToken_StoredHashed_AndScopedToTenantAndEvent()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var ev1 = await SeedEventAsync(fixture, app, Request(slug: "one"));
        var ev2 = await SeedEventAsync(fixture, app, Request(slug: "two"));
        var (r1, token1) = await RegisterAsync(fixture, app, ev1.Slug, "a@example.com");
        var (r2, token2) = await RegisterAsync(fixture, app, ev2.Slug, "a@example.com");

        var stored = await ReloadAsync(fixture, r1.Id);
        Assert.NotEqual(token1, stored.ManageTokenHash);
        Assert.Equal(Common.Tokens.Hash(token1), stored.ManageTokenHash);

        await using var db = fixture.CreateContext();
        var service = Service(fixture, db);
        var (found, foundEvent) = await service.GetByTokenAsync(app.Id, token1);
        Assert.Equal(r1.Id, found.Id);
        Assert.Equal(ev1.Id, foundEvent.Id);
        Assert.Equal(r2.Id, (await service.GetByTokenAsync(app.Id, token2)).Registration.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByTokenAsync(otherApp.Id, token1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelByTokenAsync(otherApp.Id, token1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByTokenAsync(app.Id, token1 + "x"));
        Assert.Equal(EventRegistrationStatus.Confirmed, (await ReloadAsync(fixture, r1.Id)).Status);

        // Cancelling event one's registration leaves event two's untouched.
        await service.CancelByTokenAsync(app.Id, token1);
        Assert.Equal(EventRegistrationStatus.Cancelled, (await ReloadAsync(fixture, r1.Id)).Status);
        Assert.Equal(EventRegistrationStatus.Confirmed, (await ReloadAsync(fixture, r2.Id)).Status);
    }
}
