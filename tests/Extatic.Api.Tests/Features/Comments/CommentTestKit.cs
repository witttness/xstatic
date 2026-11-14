using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Comments;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Comments;

/// <summary>Builds comment services/controllers against the fixture database.</summary>
public sealed class CommentTestKit(ApiTestFixture fixture)
{
    public CommentNotifier Notifier() => new(TestServices.EmailQueue(), TestServices.WebhookDispatcher(fixture));

    public CommentService Service(AppDbContext db) => new(db, Notifier());

    public CommentModerationService Moderation(AppDbContext db) => new(db, Notifier());

    public ClientCommentsController Client(AppDbContext db, App app, Guid? appUserId = null)
        => new(Service(db)) { ControllerContext = TestContexts.Client(app, appUserId) };

    public PlatformCommentsController Platform(AppDbContext db, App app, Guid userId)
        => new(Moderation(db)) { ControllerContext = TestContexts.Platform(app, userId) };

    /// <summary>Seeds an App with comments enabled; <paramref name="configure"/> tweaks the settings.</summary>
    public async Task<(User Owner, App App)> SeedAsync(Action<CommentSettings>? configure = null,
        bool withWebhook = false, string[]? allowedOrigins = null)
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var settings = CommentSettings.DefaultsFor(app.Id);
        settings.Enabled = true;
        configure?.Invoke(settings);
        db.CommentSettings.Add(settings);
        if (withWebhook)
            db.Webhooks.Add(new Webhook
            {
                AppId = app.Id, Url = "https://hooks.example.com/x", Secret = "s",
                Events = [.. CommentNotifier.Events]
            });
        if (allowedOrigins is not null)
        {
            var tracked = await db.Apps.FirstAsync(a => a.Id == app.Id);
            tracked.AllowedOrigins = allowedOrigins;
            app.AllowedOrigins = allowedOrigins;
        }
        await db.SaveChangesAsync();
        return (owner, app);
    }

    public async Task<AppUser> SeedAppUserAsync(Guid appId, string? displayName = "Signed In")
    {
        var user = await fixture.SeedAppUserAsync(appId);
        await using var db = fixture.CreateContext();
        var tracked = await db.AppUsers.FirstAsync(u => u.Id == user.Id);
        tracked.DisplayName = displayName;
        await db.SaveChangesAsync();
        user.DisplayName = displayName;
        return user;
    }

    public static CreateCommentRequest Anon(string body = "Nice post", string pageKey = "/blog/hello",
        Guid? parentId = null, string? website = null, string? email = "reader@example.com")
        => new(pageKey, null, "Hello", parentId, "Reader", email, body, website);

    public static CreateCommentRequest Signed(string body = "Nice post", string pageKey = "/blog/hello",
        Guid? parentId = null)
        => new(pageKey, null, "Hello", parentId, null, null, body, null);

    public async Task<Comment> CreateAsync(App app, CreateCommentRequest req, Guid? appUserId = null)
    {
        await using var db = fixture.CreateContext();
        var (comment, _) = await Service(db).CreateAsync(app, appUserId, req, "iphash", "test-agent");
        return comment;
    }

    /// <summary>Inserts a comment row directly (bypassing rules).</summary>
    public async Task<Comment> InsertAsync(App app, CommentStatus status, string pageKey = "/blog/hello",
        Comment? parent = null, Guid? appUserId = null, string body = "seeded", DateTime? createdAt = null)
    {
        await using var db = fixture.CreateContext();
        var comment = new Comment
        {
            AppId = app.Id, PageKey = pageKey, ParentId = parent?.Id, Depth = parent is null ? 1 : parent.Depth + 1,
            AppUserId = appUserId, AuthorName = "Seed", AuthorEmail = "seed-author@example.com", Body = body,
            Status = status, IpHash = "x"
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        if (createdAt is not null)
        {
            comment.CreatedAt = createdAt.Value;
            await db.SaveChangesAsync();
        }
        return comment;
    }

    public async Task<Comment?> FindAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<string>> EmailCategoriesAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        return await db.EmailOutbox.Where(m => m.AppId == appId).Select(m => m.Category).ToListAsync();
    }

    public async Task<List<(string To, string Category, string? Html)>> EmailsAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        var rows = await db.EmailOutbox.Where(m => m.AppId == appId).ToListAsync();
        return rows.Select(r => (r.ToAddress, r.Category, r.HtmlBody)).ToList();
    }

    public async Task<List<string>> WebhookEventsAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        return await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == appId).Select(l => l.EventType).ToListAsync();
    }
}
