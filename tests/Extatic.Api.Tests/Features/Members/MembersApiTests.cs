using System.Reflection;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Members;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests.Features.Members;

/// <summary>Controller-level behaviour: tenancy, auth wiring and what leaves the API.</summary>
public class MembersApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly MembersHarness h = new(fixture);

    private ClientMembersController Client(Data.AppDbContext db, App app, Guid? appUserId, Guid? tokenAppId = null)
        => new(h.Service(db)) { ControllerContext = TestContexts.Client(app, appUserId, tokenAppId: tokenAppId) };

    private MembersController Platform(Data.AppDbContext db, App app, Guid userId)
        => new(h.Service(db)) { ControllerContext = TestContexts.Platform(app, userId) };

    private static T Value<T>(ActionResult<T> result)
        => result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    [Fact]
    public async Task Directory_ExposesOnlyAllowlistedFieldsOfOptedInActiveMembers()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.With(s => s.UpdateSettingsAsync(app.Id,
            new UpdateMemberSettingsRequest(null, true, ["city"], null, null, null)));
        await h.CreatePlanAsync(app.Id);
        var listed = await fixture.SeedAppUserAsync(app.Id);
        var hidden = await fixture.SeedAppUserAsync(app.Id);
        var viewer = await fixture.SeedAppUserAsync(app.Id);
        var outsider = await fixture.SeedAppUserAsync(app.Id);
        var m = await h.JoinAsync(app.Id, listed.Id, "standard", new { city = "Leeds", phone = "07700 900123" });
        await h.JoinAsync(app.Id, hidden.Id, "standard", new { city = "Hidden-Town" });
        await h.JoinAsync(app.Id, viewer.Id, "standard");
        await h.With(s => s.UpdateNotesAsync(app.Id, m.Id, "TOP-SECRET-NOTE", owner.Id));
        await using (var db = fixture.CreateContext())
            await Client(db, app, listed.Id).UpdateDirectory(new UpdateDirectoryRequest(true));

        await using (var db = fixture.CreateContext())
        {
            var page = Value(await Client(db, app, viewer.Id).Directory());
            var json = MembersHarness.Json(page);
            var entry = Assert.Single(page.Items);
            Assert.Equal("Leeds", entry.Fields["city"].GetString());
            Assert.DoesNotContain("07700", json);
            Assert.DoesNotContain("phone", json);
            Assert.DoesNotContain("Hidden-Town", json);
            Assert.DoesNotContain("TOP-SECRET-NOTE", json);
            Assert.DoesNotContain(listed.Email!, json);
            Assert.DoesNotContain("M-0000", json);
        }

        await using (var db = fixture.CreateContext())
            await Assert.ThrowsAsync<ForbiddenException>(() => Client(db, app, outsider.Id).Directory());

        await h.With(s => s.UpdateSettingsAsync(app.Id, new UpdateMemberSettingsRequest(null, false, ["city"], null, null, null)));
        await using (var db = fixture.CreateContext())
            await Assert.ThrowsAsync<NotFoundException>(() => Client(db, app, viewer.Id).Directory());
    }

    [Fact]
    public async Task ClientMe_NeverIncludesNotesOrAdminIds()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(app.Id);
        var user = await fixture.SeedAppUserAsync(app.Id);
        await using (var db = fixture.CreateContext())
            Assert.Null(Value(await Client(db, app, user.Id).Me()).Membership);

        var m = await h.JoinAsync(app.Id, user.Id, "standard", new { city = "York" });
        await h.With(s => s.UpdateNotesAsync(app.Id, m.Id, "TOP-SECRET-NOTE", owner.Id));

        await using var db2 = fixture.CreateContext();
        var me = Value(await Client(db2, app, user.Id).Me());
        var json = MembersHarness.Json(me);
        Assert.Equal("active", me.Membership!.Status);
        Assert.Contains("York", json);
        Assert.DoesNotContain("TOP-SECRET-NOTE", json);
        Assert.DoesNotContain("notes", json);
        Assert.DoesNotContain(owner.Id.ToString(), json);
        Assert.True(Value(await Client(db2, app, user.Id).Access("standard")).Active);
        Assert.False(Value(await Client(db2, app, user.Id).Access("gold")).Active);
    }

    [Fact]
    public async Task ClientCancel_ThenMeShowsCancelled()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SubscribeAsync(app.Id);
        await h.CreatePlanAsync(app.Id);
        var user = await fixture.SeedAppUserAsync(app.Id);
        await using var db = fixture.CreateContext();
        var joined = await Client(db, app, user.Id).Join(new JoinMembershipRequest("standard", null));
        Assert.Equal(201, ((ObjectResult)joined.Result!).StatusCode);

        await using var db2 = fixture.CreateContext();
        await Client(db2, app, user.Id).Cancel();
        await using var db3 = fixture.CreateContext();
        Assert.Equal("cancelled", Value(await Client(db3, app, user.Id).Me()).Membership!.Status);
        Assert.Contains("member.cancelled", await h.WebhookEventsAsync(app.Id));
        await using var db4 = fixture.CreateContext();
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db4, app, user.Id).Cancel());
    }

    [Fact]
    public async Task AppUserOfAppA_CannotSeeOrJoinAppB()
    {
        var (_, appA, _) = await fixture.SeedAppAsync();
        var (_, appB, _) = await fixture.SeedAppAsync();
        await h.CreatePlanAsync(appA.Id, "a-plan");
        await h.CreatePlanAsync(appB.Id, "b-plan");
        var userA = await fixture.SeedAppUserAsync(appA.Id);
        var userB = await fixture.SeedAppUserAsync(appB.Id);
        await h.JoinAsync(appB.Id, userB.Id, "b-plan");

        await using var db = fixture.CreateContext();
        // A's plans list does not include B's plans.
        var plans = Value(await Client(db, appA, null).Plans());
        Assert.Equal(["a-plan"], plans.Select(p => p.Slug));
        // A token issued for App A, presented to App B, is anonymous → 401.
        var onB = Client(db, appB, userA.Id, tokenAppId: appA.Id);
        await Assert.ThrowsAsync<UnauthorizedException>(() => onB.Me());
        await Assert.ThrowsAsync<UnauthorizedException>(() => onB.Join(new JoinMembershipRequest("b-plan", null)));
        await Assert.ThrowsAsync<UnauthorizedException>(() => onB.Directory());
        // Joining B's plan through App A: plan not found.
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db, appA, userA.Id).Join(new JoinMembershipRequest("b-plan", null)));
        // Service-level: a user id from App A cannot join in App B even with B's id.
        await Assert.ThrowsAsync<NotFoundException>(() => h.JoinAsync(appB.Id, userA.Id, "b-plan"));
        Assert.Null(Value(await Client(db, appA, userA.Id).Me()).Membership);
    }

    [Fact]
    public async Task PlatformEndpoints_AreTenantScoped()
    {
        var (ownerA, appA, _) = await fixture.SeedAppAsync();
        var (_, appB, _) = await fixture.SeedAppAsync();
        var planA = await h.CreatePlanAsync(appA.Id, requiresApproval: true);
        var planB = await h.CreatePlanAsync(appB.Id);
        var userA = await fixture.SeedAppUserAsync(appA.Id);
        var userB = await fixture.SeedAppUserAsync(appB.Id);
        var memB = await h.JoinAsync(appB.Id, userB.Id, "standard");
        var memA = await h.JoinAsync(appA.Id, userA.Id, "standard");

        await using var db = fixture.CreateContext();
        var c = Platform(db, appA, ownerA.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => c.Get(memB.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Approve(memB.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Cancel(memB.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => c.UpdateNotes(memB.Id, new MembershipNotesRequest("x")));
        await Assert.ThrowsAsync<NotFoundException>(() => c.ChangePlan(memA.Id, new ChangePlanRequest(planB.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => c.GetPlan(planB.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => c.UpdatePlan(planB.Id,
            new MembershipPlanRequest("x", "x", null, null, null, null, null, null, null, null, null)));
        await Assert.ThrowsAsync<NotFoundException>(() => c.DeletePlan(planB.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Create(new CreateMembershipRequest(userB.Id, planA.Id, null, null, null)));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Create(new CreateMembershipRequest(userA.Id, planB.Id, null, null, null)));

        var list = Value(await c.List());
        Assert.Equal([memA.Id], list.Items.Select(i => i.Id));
        Assert.Equal(1, Value(await c.Stats()).Total);
        Assert.Equal([planA.Id], Value(await c.ListPlans()).Select(p => p.Id));
        Assert.Equal(MembershipStatus.Active, (await h.LoadAsync(memB.Id)).Status);

        var approved = Value(await c.Approve(memA.Id, null));
        Assert.Equal("active", approved.Membership.Status);
    }

    [Fact]
    public async Task Controllers_DeclareExpectedPolicies()
    {
        await Task.CompletedTask;
        string? Policy(Type t, string method)
            => t.GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>().Single().Policy;

        var p = typeof(MembersController);
        foreach (var m in new[] { "ListPlans", "GetPlan", "List", "Get", "Stats", "Export" })
            Assert.Equal(PolicyNames.AppAnyAccess, Policy(p, m));
        foreach (var m in new[] { "CreatePlan", "UpdatePlan", "DeletePlan", "GetSettings", "UpdateSettings" })
            Assert.Equal(PolicyNames.AppOwnerOrAdmin, Policy(p, m));
        foreach (var m in new[] { "Create", "Approve", "Reject", "Suspend", "Reinstate", "Renew", "Cancel", "ChangePlan", "UpdateNotes" })
            Assert.Equal(PolicyNames.AppOwnerOrEditor, Policy(p, m));

        var c = typeof(ClientMembersController);
        Assert.Equal(AuthSchemes.ApiKey, c.GetMethod("Plans")!.GetCustomAttribute<AuthorizeAttribute>()!.AuthenticationSchemes);
        foreach (var m in new[] { "Me", "Access", "Join", "UpdateProfile", "UpdateDirectory", "Cancel", "Directory" })
            Assert.Equal(PolicyNames.AuthenticatedAppUser, Policy(c, m));
    }

    [Fact]
    public async Task AuthenticatedAppUserPolicy_RejectsAnonymousAndForeignTokens()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, other, _) = await fixture.SeedAppAsync();
        var user = await fixture.SeedAppUserAsync(app.Id);
        var foreign = await fixture.SeedAppUserAsync(other.Id);
        using var pipeline = new TestAuthPipeline(fixture);
        var tokens = TestServices.AppUserTokenService(pipeline.Configuration);

        async Task<bool> Allowed(string? token)
        {
            var headers = token is null ? null : new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
            var ctx = pipeline.CreateContext($"/api/{app.PublicId}/members/me", headers);
            return (await pipeline.AuthorizeAsync(ctx, PolicyNames.AuthenticatedAppUser)).Succeeded;
        }

        Assert.False(await Allowed(null));
        Assert.False(await Allowed(tokens.CreateAccessToken(foreign)));
        Assert.True(await Allowed(tokens.CreateAccessToken(user)));
    }

    [Fact]
    public void Module_DeclaresWebhookEvents()
    {
        var catalog = new WebhookEventCatalog([new MembersModule()]);
        foreach (var e in new[] { "member.applied", "member.activated", "member.suspended", "member.reinstated",
                     "member.renewed", "member.expired", "member.cancelled" })
            Assert.True(catalog.IsKnown(e));
    }

    [Fact]
    public async Task Settings_RoundTripThroughController()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var c = Platform(db, app, owner.Id);
        var defaults = Value(await c.GetSettings());
        Assert.Equal([30, 7], defaults.RenewalReminderDays);
        Assert.Equal("M-", defaults.MemberNumberPrefix);

        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var saved = Value(await c.UpdateSettings(new UpdateMemberSettingsRequest(schema, true, ["city", "city"], "ABC-",
            ["Admin@Example.com"], [7, 30, 7])));
        Assert.Equal(["city"], saved.DirectoryFields);
        Assert.Equal([30, 7], saved.RenewalReminderDays);
        Assert.Equal("ABC-", saved.MemberNumberPrefix);
        Assert.True(saved.DirectoryEnabled);
        Assert.Equal(JsonValueKind.Object, saved.ProfileSchema!.Value.ValueKind);
    }
}
