using System.Reflection;
using Extatic.Api.Auth;
using Extatic.Api.Features.Comments;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Comments;

public class PlatformCommentTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly CommentTestKit kit = new(fixture);

    private static T Value<T>(ActionResult<T> result) where T : class
        => result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    private static UpdateCommentSettingsRequest Settings(bool enabled = true, string mode = "all",
        string[]? emails = null, string[]? terms = null, int maxLength = 5000, int maxDepth = 3)
        => new(enabled, mode, true, true, maxLength, maxDepth, null, terms ?? [], "pending", 2, emails ?? [], false);

    [Fact]
    public async Task Queue_DefaultsPendingFirst_FiltersAndSearches()
    {
        var (owner, app) = await kit.SeedAsync();
        await kit.InsertAsync(app, CommentStatus.Approved, body: "approved one");
        var pending = await kit.InsertAsync(app, CommentStatus.Pending, body: "needs review 100%");
        await kit.InsertAsync(app, CommentStatus.Spam, pageKey: "/other", body: "spammy");

        await using var db = fixture.CreateContext();
        var controller = kit.Platform(db, app, owner.Id);
        var all = Value(await controller.List(null, null, null));
        Assert.Equal(3, all.Total);
        Assert.Equal(pending.Id, all.Items[0].Id);
        Assert.Equal("seed-author@example.com", all.Items[0].AuthorEmail);

        Assert.Equal("spammy", Assert.Single(Value(await controller.List("spam", null, null)).Items).Body);
        Assert.Equal("spammy", Assert.Single(Value(await controller.List(null, "/other", null)).Items).Body);
        Assert.Equal(pending.Id, Assert.Single(Value(await controller.List(null, null, "100%")).Items).Id);
        Assert.Empty(Value(await controller.List(null, null, "%%nothing")).Items);
        await Assert.ThrowsAsync<ValidationException>(() => controller.List("bogus", null, null));
    }

    [Fact]
    public async Task Moderate_SingleAndBulk_StagesEvents_AndReplyEmail()
    {
        var (owner, app) = await kit.SeedAsync(withWebhook: true);
        var parentAuthor = await kit.SeedAppUserAsync(app.Id);
        var parent = await kit.InsertAsync(app, CommentStatus.Approved, appUserId: parentAuthor.Id);
        var reply = await kit.InsertAsync(app, CommentStatus.Pending, parent: parent);
        var a = await kit.InsertAsync(app, CommentStatus.Pending);
        var b = await kit.InsertAsync(app, CommentStatus.Pending);

        await using var db = fixture.CreateContext();
        var controller = kit.Platform(db, app, owner.Id);
        var approved = Value(await controller.Approve(reply.Id));
        Assert.Equal("approved", approved.Status);
        Assert.Equal(owner.Id, approved.ModeratedByUserId);
        Assert.Equal(parentAuthor.Email, Assert.Single(await kit.EmailsAsync(app.Id), e => e.Category == "comments.reply").To);

        var bulk = Value(await controller.Bulk(new BulkModerateRequest([a.Id, b.Id, reply.Id, Guid.NewGuid()], "reject")));
        Assert.Equal(3, bulk.Updated);
        Assert.Equal(1, bulk.NotFound);
        Assert.Equal("spam", Value(await controller.Spam(a.Id)).Status);

        var events = await kit.WebhookEventsAsync(app.Id);
        Assert.Equal(1, events.Count(e => e == "comment.approved"));
        Assert.Equal(4, events.Count(e => e == "comment.rejected"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Bulk(new BulkModerateRequest(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList(), "approve")));
        await Assert.ThrowsAsync<ValidationException>(() => controller.Bulk(new BulkModerateRequest([a.Id], "delete")));
    }

    [Fact]
    public async Task ConcurrentApprove_NotifiesOnce()
    {
        var (owner, app) = await kit.SeedAsync(withWebhook: true);
        var parentAuthor = await kit.SeedAppUserAsync(app.Id);
        var parent = await kit.InsertAsync(app, CommentStatus.Approved, appUserId: parentAuthor.Id);
        var reply = await kit.InsertAsync(app, CommentStatus.Pending, parent: parent);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            await kit.Moderation(db).ModerateAsync(app, owner.Id, [reply.Id], CommentStatus.Approved);
        })));

        Assert.Single(await kit.EmailsAsync(app.Id), e => e.Category == "comments.reply");
        Assert.Single(await kit.WebhookEventsAsync(app.Id), e => e == "comment.approved");
    }

    [Fact]
    public async Task CrossTenant_ModerationEndpoints_Return404()
    {
        var (owner, app) = await kit.SeedAsync();
        var (otherOwner, other) = await kit.SeedAsync();
        var foreign = await kit.InsertAsync(other, CommentStatus.Pending, body: "foreign");
        await kit.InsertAsync(app, CommentStatus.Pending, body: "mine");

        await using var db = fixture.CreateContext();
        var controller = kit.Platform(db, app, owner.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Approve(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Reject(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Spam(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Delete(foreign.Id));
        var bulk = Value(await controller.Bulk(new BulkModerateRequest([foreign.Id], "approve")));
        Assert.Equal(0, bulk.Updated);
        Assert.DoesNotContain(Value(await controller.List(null, null, null)).Items, c => c.Body == "foreign");
        Assert.Equal(1, Value(await controller.Stats()).Pending);
        Assert.DoesNotContain(Value(await controller.Pages()).Items, p => p.Total > 1);

        Assert.Equal(CommentStatus.Pending, (await kit.FindAsync(foreign.Id))!.Status);
        _ = otherOwner;
    }

    [Fact]
    public async Task HardDelete_RemovesThread()
    {
        var (owner, app) = await kit.SeedAsync();
        var parent = await kit.InsertAsync(app, CommentStatus.Approved);
        var reply = await kit.InsertAsync(app, CommentStatus.Approved, parent: parent);
        await using var db = fixture.CreateContext();
        Assert.IsType<NoContentResult>(await kit.Platform(db, app, owner.Id).Delete(parent.Id));
        Assert.Null(await kit.FindAsync(parent.Id));
        Assert.Null(await kit.FindAsync(reply.Id));
    }

    [Fact]
    public async Task Settings_DefaultsValidationAndUpdate()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = kit.Platform(db, app, owner.Id);
        var defaults = Value(await controller.GetSettings());
        Assert.False(defaults.Enabled);
        Assert.Equal("all", defaults.ModerationMode);
        Assert.Equal(5000, defaults.MaxLength);
        Assert.Equal(3, defaults.MaxDepth);
        Assert.Equal(2, defaults.MaxLinks);
        Assert.True(defaults.AllowAnonymous);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            controller.UpdateSettings(Settings(emails: ["nope"], maxLength: 0, maxDepth: 11)));
        Assert.Contains(ex.Errors, e => e.Pointer == "notify_emails/0");
        Assert.Contains(ex.Errors, e => e.Pointer == "max_length");
        Assert.Contains(ex.Errors, e => e.Pointer == "max_depth");
        await Assert.ThrowsAsync<ValidationException>(() => controller.UpdateSettings(Settings(mode: "sometimes")));

        var saved = Value(await controller.UpdateSettings(
            Settings(mode: "anonymous_only", emails: ["mod@example.com"], terms: [" casino ", "Casino", ""])));
        Assert.True(saved.Enabled);
        Assert.Equal("anonymous_only", saved.ModerationMode);
        Assert.Equal(["casino"], saved.BlockedTerms);
        Assert.Equal(["mod@example.com"], saved.NotifyEmails);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task Settings_ConcurrentFirstSave_CreatesOneRow()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            await kit.Moderation(db).UpdateSettingsAsync(app.Id, Settings(maxLength: 100 + i));
        })));
        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.CommentSettings.CountAsync(s => s.AppId == app.Id));
    }

    [Fact]
    public async Task Pages_ListCounts_CloseReopen_ConcurrentCloseCreatesOneRow()
    {
        var (owner, app) = await kit.SeedAsync();
        await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/a");
        await kit.InsertAsync(app, CommentStatus.Pending, pageKey: "/a");
        await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/b");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            await kit.Moderation(db).SetPageClosedAsync(app.Id, "/a", closed: true);
        })));

        await using var ctx = fixture.CreateContext();
        Assert.Equal(1, await ctx.CommentPages.CountAsync(p => p.AppId == app.Id && p.PageKey == "/a"));
        var controller = kit.Platform(ctx, app, owner.Id);
        var pages = Value(await controller.Pages());
        Assert.Equal(2, pages.Total);
        var a = Assert.Single(pages.Items, p => p.PageKey == "/a");
        Assert.True(a.IsClosed);
        Assert.Equal(2, a.Total);
        Assert.Equal(1, a.Approved);
        Assert.Equal(1, a.Pending);
        Assert.False(Assert.Single(pages.Items, p => p.PageKey == "/b").IsClosed);

        await controller.ReopenPage(new PageKeyRequest("/a"));
        Assert.False(Value(await kit.Platform(ctx, app, owner.Id).Pages()).Items.Single(p => p.PageKey == "/a").IsClosed);
        await Assert.ThrowsAsync<ValidationException>(() => controller.ClosePage(new PageKeyRequest("  ")));
    }

    [Fact]
    public async Task Stats_CountByStatus()
    {
        var (owner, app) = await kit.SeedAsync();
        await kit.InsertAsync(app, CommentStatus.Pending);
        await kit.InsertAsync(app, CommentStatus.Pending);
        await kit.InsertAsync(app, CommentStatus.Spam);
        await using var db = fixture.CreateContext();
        var stats = Value(await kit.Platform(db, app, owner.Id).Stats());
        Assert.Equal(new CommentStatsDto(2, 0, 0, 1), stats);
    }

    [Theory]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.List), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Get), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Approve), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Reject), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Spam), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Bulk), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Delete), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.GetSettings), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.UpdateSettings), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Pages), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.ClosePage), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.ReopenPage), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(PlatformCommentsController), nameof(PlatformCommentsController.Stats), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(ClientCommentsController), nameof(ClientCommentsController.List), PolicyNames.ClientAnonymousOrAppUser)]
    [InlineData(typeof(ClientCommentsController), nameof(ClientCommentsController.Counts), PolicyNames.ClientAnonymousOrAppUser)]
    [InlineData(typeof(ClientCommentsController), nameof(ClientCommentsController.Create), PolicyNames.ClientAnonymousOrAppUser)]
    [InlineData(typeof(ClientCommentsController), nameof(ClientCommentsController.Update), PolicyNames.AuthenticatedAppUser)]
    [InlineData(typeof(ClientCommentsController), nameof(ClientCommentsController.Delete), PolicyNames.AuthenticatedAppUser)]
    public void Endpoints_UseExpectedPolicies(Type controller, string action, string policy)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        if (controller == typeof(ClientCommentsController))
            Assert.NotNull(method.GetCustomAttribute<EnableRateLimitingAttribute>());
        if (action == nameof(ClientCommentsController.Create) && controller == typeof(ClientCommentsController))
            Assert.Equal(RateLimitPolicyNames.ClientAnonymousWrites,
                method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }
}
