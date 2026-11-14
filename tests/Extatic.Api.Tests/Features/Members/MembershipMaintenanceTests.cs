using Extatic.Api.Features.Members;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Members;

/// <summary>
/// The worker processes every App in the (per-class) database, so assertions are made on the
/// test's own App (its emails, webhooks and membership rows) rather than on global counts.
/// </summary>
public class MembershipMaintenanceTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly MembersHarness h = new(fixture);

    private async Task RunAsync()
    {
        await using var db = fixture.CreateContext();
        await h.Maintenance(db).RunAsync();
    }

    private async Task<int> EmailsAsync(Guid appId, string category)
        => (await h.EmailCategoriesAsync(appId)).Count(c => c == category);

    [Fact]
    public async Task Expiry_RunsOnce_EvenWhenRunTwice()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.With(s => s.UpdateSettingsAsync(app.Id, new UpdateMemberSettingsRequest(null, false, null, null, null, [])));
        await h.CreatePlanAsync(app.Id, durationDays: 10);
        var user = await fixture.SeedAppUserAsync(app.Id);
        var m = await h.JoinAsync(app.Id, user.Id, "standard");

        await RunAsync();
        Assert.Equal(MembershipStatus.Active, (await h.LoadAsync(m.Id)).Status);
        // Past expiry but before the worker ran: access is already denied.
        h.Clock.Advance(TimeSpan.FromDays(10) + TimeSpan.FromMinutes(1));
        Assert.False(await h.With(s => s.IsActiveMemberAsync(app.Id, user.Id)));

        await RunAsync();
        await RunAsync();

        Assert.Equal(MembershipStatus.Expired, (await h.LoadAsync(m.Id)).Status);
        Assert.Equal(1, await EmailsAsync(app.Id, "members.expired"));
        Assert.Equal(0, await EmailsAsync(app.Id, "members.renewal_reminder"));
        Assert.Single(await h.WebhookEventsAsync(app.Id), e => e == "member.expired");
        var detail = await h.With(s => s.GetDetailAsync(app.Id, m.Id));
        Assert.Equal("expired", detail.Events.Last().Type);
    }

    [Fact]
    public async Task Reminders_SentOncePerOffset()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id, durationDays: 60);
        var user = await fixture.SeedAppUserAsync(app.Id);
        var m = await h.JoinAsync(app.Id, user.Id, "standard");

        await RunAsync();                                                 // 60 days left
        Assert.Equal(0, await EmailsAsync(app.Id, "members.renewal_reminder"));
        h.Clock.Advance(TimeSpan.FromDays(31));                           // 29 days left → 30-day reminder
        await RunAsync();
        await RunAsync();
        Assert.Equal(1, await EmailsAsync(app.Id, "members.renewal_reminder"));
        h.Clock.Advance(TimeSpan.FromDays(10));                           // 19 days left → nothing new
        await RunAsync();
        Assert.Equal(1, await EmailsAsync(app.Id, "members.renewal_reminder"));
        h.Clock.Advance(TimeSpan.FromDays(13));                           // 6 days left → 7-day reminder
        await RunAsync();
        await RunAsync();
        Assert.Equal(2, await EmailsAsync(app.Id, "members.renewal_reminder"));

        var saved = await h.LoadAsync(m.Id);
        Assert.Equal(new[] { 7, 30 }, saved.RemindersSent.Order());
    }

    [Fact]
    public async Task Reminders_ShortTerm_SendsOnlyNearestOffset_AndRenewResets()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id, durationDays: 5);
        var user = await fixture.SeedAppUserAsync(app.Id);
        var m = await h.JoinAsync(app.Id, user.Id, "standard");

        await RunAsync();
        var first = await h.LoadAsync(m.Id);
        Assert.Equal(new[] { 7, 30 }, first.RemindersSent.Order());
        await using (var db = fixture.CreateContext())
        {
            var email = await db.EmailOutbox.SingleAsync(e => e.AppId == app.Id && e.Category == "members.renewal_reminder");
            Assert.Contains("expires in 5 days", email.Subject);
        }

        await h.With(s => s.TransitionAsync(app.Id, m.Id, MembershipAction.Renew, owner.Id, null));
        Assert.Empty((await h.LoadAsync(m.Id)).RemindersSent);
        // New term ends in 10 days: the 30-day offset is due again, once, for the new term.
        await RunAsync();
        await RunAsync();
        Assert.Equal(2, await EmailsAsync(app.Id, "members.renewal_reminder"));
        var after = await h.LoadAsync(m.Id);
        Assert.Equal(new[] { 30 }, after.RemindersSent);
    }

    [Fact]
    public async Task ParallelWorkers_ClaimEachMembershipOnce()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.CreatePlanAsync(app.Id, durationDays: 20);
        for (var i = 0; i < 5; i++)
            await h.JoinAsync(app.Id, (await fixture.SeedAppUserAsync(app.Id)).Id, "standard");
        h.Clock.Advance(TimeSpan.FromDays(19));       // both offsets due → one reminder each

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync()));
        Assert.Equal(5, await EmailsAsync(app.Id, "members.renewal_reminder"));

        h.Clock.Advance(TimeSpan.FromDays(2));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync()));
        Assert.Equal(5, await EmailsAsync(app.Id, "members.expired"));
        Assert.Equal(5, (await h.WebhookEventsAsync(app.Id)).Count(e => e == "member.expired"));
        Assert.Equal(5, await EmailsAsync(app.Id, "members.renewal_reminder"));
    }
}
