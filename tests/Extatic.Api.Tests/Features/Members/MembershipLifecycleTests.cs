using Extatic.Api.Features.Members;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Members;

public class MembershipLifecycleTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly MembersHarness h = new(fixture);

    private async Task<(Guid AppId, Guid OwnerId, Membership M)> SetupAsync(bool requiresApproval, int? days = 365)
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.CreatePlanAsync(app.Id, requiresApproval: requiresApproval, durationDays: days);
        var user = await fixture.SeedAppUserAsync(app.Id);
        var m = await h.JoinAsync(app.Id, user.Id, "standard");
        return (app.Id, owner.Id, m);
    }

    private Task<Membership> Do(Guid appId, Guid ownerId, Guid id, MembershipAction a, string? reason = null)
        => h.With(s => s.TransitionAsync(appId, id, a, ownerId, reason));

    [Theory]
    [InlineData(MembershipStatus.Pending, MembershipAction.Approve, true)]
    [InlineData(MembershipStatus.Pending, MembershipAction.Reject, true)]
    [InlineData(MembershipStatus.Pending, MembershipAction.Cancel, true)]
    [InlineData(MembershipStatus.Pending, MembershipAction.Suspend, false)]
    [InlineData(MembershipStatus.Pending, MembershipAction.Renew, false)]
    [InlineData(MembershipStatus.Active, MembershipAction.Suspend, true)]
    [InlineData(MembershipStatus.Active, MembershipAction.Cancel, true)]
    [InlineData(MembershipStatus.Active, MembershipAction.Expire, true)]
    [InlineData(MembershipStatus.Active, MembershipAction.Renew, true)]
    [InlineData(MembershipStatus.Active, MembershipAction.Approve, false)]
    [InlineData(MembershipStatus.Active, MembershipAction.Reinstate, false)]
    [InlineData(MembershipStatus.Suspended, MembershipAction.Reinstate, true)]
    [InlineData(MembershipStatus.Suspended, MembershipAction.Cancel, true)]
    [InlineData(MembershipStatus.Suspended, MembershipAction.Renew, false)]
    [InlineData(MembershipStatus.Expired, MembershipAction.Renew, true)]
    [InlineData(MembershipStatus.Expired, MembershipAction.Reinstate, false)]
    [InlineData(MembershipStatus.Cancelled, MembershipAction.Renew, false)]
    [InlineData(MembershipStatus.Cancelled, MembershipAction.Approve, false)]
    [InlineData(MembershipStatus.Rejected, MembershipAction.Approve, false)]
    public void TransitionTable(MembershipStatus from, MembershipAction action, bool allowed)
        => Assert.Equal(allowed, MembershipService.CanTransition(from, action));

    [Fact]
    public async Task Approve_ActivatesWithNumberAuditEmailAndWebhook()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: true);

        await Do(appId, ownerId, m.Id, MembershipAction.Approve);

        var detail = await h.With(s => s.GetDetailAsync(appId, m.Id));
        Assert.Equal("active", detail.Membership.Status);
        Assert.Equal("M-00001", detail.Membership.MemberNumber);
        Assert.Equal(ownerId, detail.ApprovedByUserId);
        Assert.Equal(["applied", "approve"], detail.Events.Select(e => e.Type));
        Assert.Equal("pending", detail.Events[1].FromStatus);
        Assert.Equal(["members.application_received", "members.approved"], await h.EmailCategoriesAsync(appId));
        Assert.Equal(["member.activated", "member.applied"], await h.WebhookEventsAsync(appId));
    }

    [Fact]
    public async Task Reject_IsTerminal()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: true);
        await Do(appId, ownerId, m.Id, MembershipAction.Reject, "Not eligible <b>");

        Assert.Equal(MembershipStatus.Rejected, (await h.LoadAsync(m.Id)).Status);
        await Assert.ThrowsAsync<ConflictException>(() => Do(appId, ownerId, m.Id, MembershipAction.Approve));
        await using var db = fixture.CreateContext();
        var email = await db.EmailOutbox.SingleAsync(e => e.AppId == appId && e.Category == "members.rejected");
        Assert.Contains("Not eligible &lt;b&gt;", email.HtmlBody);
    }

    [Fact]
    public async Task SuspendReinstateCancel_FlowWithWebhooks()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: false);

        await Do(appId, ownerId, m.Id, MembershipAction.Suspend);
        Assert.False(await h.With(s => s.IsActiveMemberAsync(appId, m.AppUserId)));
        await Assert.ThrowsAsync<ConflictException>(() => Do(appId, ownerId, m.Id, MembershipAction.Suspend));
        // A suspended member cannot cancel themselves out of the suspension.
        await Assert.ThrowsAsync<ConflictException>(() => h.With(s => s.MemberCancelAsync(appId, m.AppUserId)));
        await Do(appId, ownerId, m.Id, MembershipAction.Reinstate);
        Assert.True(await h.With(s => s.IsActiveMemberAsync(appId, m.AppUserId)));
        await Do(appId, ownerId, m.Id, MembershipAction.Cancel);

        var saved = await h.LoadAsync(m.Id);
        Assert.Equal(MembershipStatus.Cancelled, saved.Status);
        Assert.NotNull(saved.CancelledAt);
        await Assert.ThrowsAsync<ConflictException>(() => Do(appId, ownerId, m.Id, MembershipAction.Reinstate));
        Assert.Equal(["member.activated", "member.applied", "member.cancelled", "member.reinstated", "member.suspended"],
            await h.WebhookEventsAsync(appId));
        Assert.Equal(["members.approved", "members.cancelled", "members.reinstated", "members.suspended"],
            await h.EmailCategoriesAsync(appId));
    }

    [Fact]
    public async Task Renew_ActiveExtendsFromCurrentExpiry_ExpiredExtendsFromNow()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: false, days: 30);
        var before = await h.LoadAsync(m.Id);

        await Do(appId, ownerId, m.Id, MembershipAction.Renew);
        var renewed = await h.LoadAsync(m.Id);
        Assert.Equal(before.ExpiresAt!.Value.AddDays(30), renewed.ExpiresAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Empty(renewed.RemindersSent);

        // Let it lapse, expire it, then renew: the new term runs from "now".
        h.Clock.Advance(TimeSpan.FromDays(61));
        await using (var db = fixture.CreateContext())
            await h.Maintenance(db).ExpireDueAsync();
        Assert.Equal(MembershipStatus.Expired, (await h.LoadAsync(m.Id)).Status);
        await Do(appId, ownerId, m.Id, MembershipAction.Renew);
        var again = await h.LoadAsync(m.Id);
        Assert.Equal(MembershipStatus.Active, again.Status);
        Assert.Equal(h.Clock.Now.UtcDateTime.AddDays(30), again.ExpiresAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal("M-00001", again.MemberNumber);
        Assert.Contains("member.renewed", await h.WebhookEventsAsync(appId));
    }

    [Fact]
    public void RenewedExpiry_Maths()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddDays(10 + 365), MembershipService.RenewedExpiry(now.AddDays(10), 365, now));
        Assert.Equal(now.AddDays(365), MembershipService.RenewedExpiry(now.AddDays(-10), 365, now));
        Assert.Equal(now.AddDays(365), MembershipService.RenewedExpiry(null, 365, now));
    }

    [Fact]
    public async Task Renew_PlanWithoutDuration_IsConflict()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: false, days: null);
        Assert.Null((await h.LoadAsync(m.Id)).ExpiresAt);
        await Assert.ThrowsAsync<ConflictException>(() => Do(appId, ownerId, m.Id, MembershipAction.Renew));
    }

    [Fact]
    public async Task Renew_ExpiredWhileUserHasNewerOpenMembership_IsConflict()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: false, days: 10);
        h.Clock.Advance(TimeSpan.FromDays(11));
        await using (var db = fixture.CreateContext()) await h.Maintenance(db).ExpireDueAsync();
        await h.JoinAsync(appId, m.AppUserId, "standard");

        await Assert.ThrowsAsync<ConflictException>(() => Do(appId, ownerId, m.Id, MembershipAction.Renew));
    }

    [Fact]
    public async Task ChangePlan_MovesMembershipAndRespectsCapacity()
    {
        var (appId, ownerId, m) = await SetupAsync(requiresApproval: false);
        var gold = await h.CreatePlanAsync(appId, "gold", maxMembers: 1);
        var other = await fixture.SeedAppUserAsync(appId);
        await h.JoinAsync(appId, other.Id, "gold");

        await Assert.ThrowsAsync<ConflictException>(() => h.With(s => s.ChangePlanAsync(appId, m.Id, gold.Id, ownerId)));
        var silver = await h.CreatePlanAsync(appId, "silver");
        await h.With(s => s.ChangePlanAsync(appId, m.Id, silver.Id, ownerId));
        var detail = await h.With(s => s.GetDetailAsync(appId, m.Id));
        Assert.Equal("silver", detail.Membership.PlanSlug);
        Assert.Equal("standard -> silver", detail.Events.Last().Detail);
    }

    [Fact]
    public async Task AdminCreate_ActiveOrPending_AndOnePerUser()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var plan = await h.CreatePlanAsync(app.Id, selfSignup: false);
        var u1 = await fixture.SeedAppUserAsync(app.Id);
        var u2 = await fixture.SeedAppUserAsync(app.Id);

        var a = await h.With(s => s.CreateAsync(app.Id, owner.Id, new CreateMembershipRequest(u1.Id, plan.Id, null, null, "VIP")));
        var p = await h.With(s => s.CreateAsync(app.Id, owner.Id, new CreateMembershipRequest(u2.Id, plan.Id, "pending", null, null)));

        Assert.Equal(MembershipStatus.Active, (await h.LoadAsync(a.Id)).Status);
        Assert.Equal(MembershipStatus.Pending, (await h.LoadAsync(p.Id)).Status);
        await Assert.ThrowsAsync<ConflictException>(() =>
            h.With(s => s.CreateAsync(app.Id, owner.Id, new CreateMembershipRequest(u1.Id, plan.Id, null, null, null))));
        await Assert.ThrowsAsync<ValidationException>(() =>
            h.With(s => s.CreateAsync(app.Id, owner.Id, new CreateMembershipRequest(u1.Id, plan.Id, "expired", null, null))));
    }

    [Fact]
    public async Task PlanDelete_WithMemberships_IsConflict_DuplicateSlugConflict_InvalidIs422()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var plan = await h.CreatePlanAsync(app.Id);
        var empty = await h.CreatePlanAsync(app.Id, "empty");
        await h.JoinAsync(app.Id, (await fixture.SeedAppUserAsync(app.Id)).Id, "standard");

        await Assert.ThrowsAsync<ConflictException>(() => h.With(s => s.DeletePlanAsync(app.Id, plan.Id)));
        await h.With(s => s.DeletePlanAsync(app.Id, empty.Id));
        await Assert.ThrowsAsync<ConflictException>(() => h.CreatePlanAsync(app.Id));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.With(s => s.CreatePlanAsync(app.Id,
            new MembershipPlanRequest("", "Bad Slug", null, null, 0, null, null, 0, null, null, null))));
        Assert.Equal(["name", "slug", "duration_days", "max_members"], ex.Errors.Select(e => e.Pointer));
    }

    [Fact]
    public async Task ListSearchStatsAndExport()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var plan = await h.CreatePlanAsync(app.Id, durationDays: 20);
        var u = await fixture.SeedAppUserAsync(app.Id);
        await using (var db = fixture.CreateContext())
        {
            var tracked = await db.AppUsers.SingleAsync(x => x.Id == u.Id);
            tracked.DisplayName = "Grace_Hopper";
            await db.SaveChangesAsync();
        }
        var m = await h.JoinAsync(app.Id, u.Id, "standard", new { city = "=cmd" });
        await h.JoinAsync(app.Id, (await fixture.SeedAppUserAsync(app.Id)).Id, "standard");
        await h.With(s => s.UpdateNotesAsync(app.Id, m.Id, "private note", owner.Id));

        var found = await h.With(s => s.ListAsync(app.Id, "active", plan.Id, "grace_", 1, 20));
        Assert.Equal(m.Id, Assert.Single(found.Items).Id);
        Assert.Single((await h.With(s => s.ListAsync(app.Id, null, null, "M-00001", 1, 20))).Items);
        Assert.Empty((await h.With(s => s.ListAsync(app.Id, "pending", null, null, 1, 20))).Items);
        await Assert.ThrowsAsync<BadRequestException>(() => h.With(s => s.ListAsync(app.Id, "bogus", null, null, 1, 20)));

        var stats = await h.With(s => s.GetStatsAsync(app.Id));
        Assert.Equal(2, stats.Total);
        Assert.Equal(2, stats.ByStatus["active"]);
        Assert.Equal(2, stats.ExpiringSoon);
        Assert.Equal(2, stats.ByPlan.Single().ByStatus["active"]);

        var csv = System.Text.Encoding.UTF8.GetString(await h.With(s => s.ExportCsvAsync(app.Id, null, null, null)));
        Assert.Contains("Grace_Hopper", csv);
        Assert.DoesNotContain("private note", csv);
    }
}
