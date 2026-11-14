using System.Text.Json;
using Extatic.Api.Features.Members;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Members;

public class MembershipJoinTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly MembersHarness h = new(fixture);

    [Fact]
    public async Task Join_PlanWithoutApproval_IsActiveWithNumberExpiryEmailsAndWebhooks()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.With(s => s.UpdateSettingsAsync(app.Id, new UpdateMemberSettingsRequest(
            null, false, null, "SC-", ["board@example.com"], [30, 7])));
        await h.CreatePlanAsync(app.Id, durationDays: 365);
        var user = await fixture.SeedAppUserAsync(app.Id);

        var m = await h.JoinAsync(app.Id, user.Id, "standard", new { city = "Leeds" });

        var saved = await h.LoadAsync(m.Id);
        Assert.Equal(MembershipStatus.Active, saved.Status);
        Assert.Equal("SC-00001", saved.MemberNumber);
        Assert.NotNull(saved.StartsAt);
        Assert.Equal(saved.StartsAt!.Value.AddDays(365), saved.ExpiresAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(["members.approved", "members.new_application"], await h.EmailCategoriesAsync(app.Id));
        Assert.Equal(["member.activated", "member.applied"], await h.WebhookEventsAsync(app.Id));
        Assert.True(await h.With(s => s.IsActiveMemberAsync(app.Id, user.Id)));
        Assert.True(await h.With(s => s.IsActiveMemberAsync(app.Id, user.Id, "standard")));
        Assert.False(await h.With(s => s.IsActiveMemberAsync(app.Id, user.Id, "other")));
    }

    [Fact]
    public async Task Join_PlanRequiringApproval_IsPending_NoNumberYet()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.CreatePlanAsync(app.Id, requiresApproval: true);
        var user = await fixture.SeedAppUserAsync(app.Id);

        var m = await h.JoinAsync(app.Id, user.Id, "standard");

        var saved = await h.LoadAsync(m.Id);
        Assert.Equal(MembershipStatus.Pending, saved.Status);
        Assert.Null(saved.MemberNumber);
        Assert.Null(saved.ExpiresAt);
        Assert.Equal(["members.application_received"], await h.EmailCategoriesAsync(app.Id));
        Assert.Equal(["member.applied"], await h.WebhookEventsAsync(app.Id));
        Assert.False(await h.With(s => s.IsActiveMemberAsync(app.Id, user.Id)));
    }

    [Fact]
    public async Task Join_RejectsInactiveUnknownAndClosedPlans()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id, "inactive", isActive: false);
        await h.CreatePlanAsync(app.Id, "invite-only", selfSignup: false);
        var user = await fixture.SeedAppUserAsync(app.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => h.JoinAsync(app.Id, user.Id, "inactive"));
        await Assert.ThrowsAsync<NotFoundException>(() => h.JoinAsync(app.Id, user.Id, "nope"));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.JoinAsync(app.Id, user.Id, "invite-only"));
        await Assert.ThrowsAsync<ValidationException>(() => h.JoinAsync(app.Id, user.Id, ""));
    }

    [Fact]
    public async Task Join_SecondOpenMembership_IsConflict_AndLeavesNoStagedSideEffects()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.CreatePlanAsync(app.Id, "a");
        await h.CreatePlanAsync(app.Id, "b");
        var user = await fixture.SeedAppUserAsync(app.Id);
        await h.JoinAsync(app.Id, user.Id, "a");
        var emails = (await h.EmailCategoriesAsync(app.Id)).Count;
        var hooks = (await h.WebhookEventsAsync(app.Id)).Count;

        await Assert.ThrowsAsync<ConflictException>(() => h.JoinAsync(app.Id, user.Id, "b"));

        Assert.Equal(emails, (await h.EmailCategoriesAsync(app.Id)).Count);
        Assert.Equal(hooks, (await h.WebhookEventsAsync(app.Id)).Count);
    }

    [Fact]
    public async Task Join_AfterCancelling_IsAllowed()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id);
        var user = await fixture.SeedAppUserAsync(app.Id);
        await h.JoinAsync(app.Id, user.Id, "standard");
        await h.With(s => s.MemberCancelAsync(app.Id, user.Id));

        var again = await h.JoinAsync(app.Id, user.Id, "standard");

        Assert.Equal(MembershipStatus.Active, again.Status);
        Assert.Equal("M-00002", (await h.LoadAsync(again.Id)).MemberNumber);
    }

    [Fact]
    public async Task Join_SameUserInParallel_OnlyOneSucceeds()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id);
        var user = await fixture.SeedAppUserAsync(app.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await h.JoinAsync(app.Id, user.Id, "standard"); return true; }
            catch (ConflictException) { return false; }
        }));

        Assert.Equal(1, results.Count(r => r));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Memberships.CountAsync(m => m.AppId == app.Id && m.AppUserId == user.Id));
    }

    [Fact]
    public async Task MaxMembers_IsEnforcedUnderParallelJoins()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id, maxMembers: 3);
        var users = new List<Guid>();
        for (var i = 0; i < 12; i++) users.Add((await fixture.SeedAppUserAsync(app.Id)).Id);

        var results = await Task.WhenAll(users.Select(async u =>
        {
            try { await h.JoinAsync(app.Id, u, "standard"); return true; }
            catch (ConflictException) { return false; }
        }));

        Assert.Equal(3, results.Count(r => r));
        await using var db = fixture.CreateContext();
        Assert.Equal(3, await db.Memberships.CountAsync(m => m.AppId == app.Id && m.Status == MembershipStatus.Active));
        var plans = await h.With(s => s.ListClientPlansAsync(app.Id));
        Assert.True(plans.Single().IsFull);
    }

    [Fact]
    public async Task MaxMembers_IsEnforcedOnApprovalAndReinstate()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id, requiresApproval: true, maxMembers: 1);
        var a = await h.JoinAsync(app.Id, (await fixture.SeedAppUserAsync(app.Id)).Id, "standard");
        var b = await h.JoinAsync(app.Id, (await fixture.SeedAppUserAsync(app.Id)).Id, "standard");

        await h.With(s => s.TransitionAsync(app.Id, a.Id, MembershipAction.Approve, owner.Id, null));
        await Assert.ThrowsAsync<ConflictException>(() =>
            h.With(s => s.TransitionAsync(app.Id, b.Id, MembershipAction.Approve, owner.Id, null)));

        await h.With(s => s.TransitionAsync(app.Id, a.Id, MembershipAction.Suspend, owner.Id, null));
        await h.With(s => s.TransitionAsync(app.Id, b.Id, MembershipAction.Approve, owner.Id, null));
        await Assert.ThrowsAsync<ConflictException>(() =>
            h.With(s => s.TransitionAsync(app.Id, a.Id, MembershipAction.Reinstate, owner.Id, null)));
    }

    [Fact]
    public async Task MemberNumbers_AreUniqueAndSequentialUnderParallelActivations()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id);
        var users = new List<Guid>();
        for (var i = 0; i < 20; i++) users.Add((await fixture.SeedAppUserAsync(app.Id)).Id);

        await Task.WhenAll(users.Select(u => h.JoinAsync(app.Id, u, "standard")));

        await using var db = fixture.CreateContext();
        var numbers = await db.Memberships.Where(m => m.AppId == app.Id).Select(m => m.MemberNumber!).ToListAsync();
        Assert.Equal(20, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"M-{n:D5}").Order(), numbers.Order());
        Assert.Equal(21, (await db.MemberSettings.SingleAsync(s => s.AppId == app.Id)).NextMemberNumber);
    }

    [Fact]
    public async Task ProfileSchema_IsEnforcedOnJoinAndProfileUpdate()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "name" },
            properties = new { name = new { type = "string", minLength = 1 }, age = new { type = "integer" } }
        });
        await h.With(s => s.UpdateSettingsAsync(app.Id, new UpdateMemberSettingsRequest(schema, false, null, null, null, null)));
        await h.CreatePlanAsync(app.Id);
        var user = await fixture.SeedAppUserAsync(app.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.JoinAsync(app.Id, user.Id, "standard", new { age = 3 }));
        Assert.All(ex.Errors, e => Assert.StartsWith("profile", e.Pointer));
        await Assert.ThrowsAsync<ValidationException>(() => h.JoinAsync(app.Id, user.Id, "standard", new[] { 1, 2 }));

        await h.JoinAsync(app.Id, user.Id, "standard", new { name = "Ada" });
        var bad = await Assert.ThrowsAsync<ValidationException>(() => h.With(s =>
            s.UpdateProfileAsync(app.Id, user.Id, JsonSerializer.SerializeToElement(new { name = "Ada", age = "old" }))));
        Assert.Contains(bad.Errors, e => e.Pointer == "profile/age");

        var ok = await h.With(s => s.UpdateProfileAsync(app.Id, user.Id,
            JsonSerializer.SerializeToElement(new { name = "Ada L", age = 36 })));
        Assert.Contains("Ada L", ok.Profile);
    }

    [Fact]
    public async Task Settings_RejectInvalidSchemaEmailsAndOffsets()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.With(s => s.UpdateSettingsAsync(app.Id,
            new UpdateMemberSettingsRequest(JsonSerializer.SerializeToElement(new { type = 5 }), true, ["city"],
                "bad prefix!", ["not-an-email"], [0, 400]))));
        Assert.Contains(ex.Errors, e => e.Pointer == "profile_schema");
        Assert.Contains(ex.Errors, e => e.Pointer == "member_number_prefix");
        Assert.Contains(ex.Errors, e => e.Pointer == "notify_emails/0");
        Assert.Contains(ex.Errors, e => e.Pointer == "renewal_reminder_days");
    }
}
