using System.Net;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Notifications;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Comments;

/// <summary>
/// Stages comment emails and webhooks on the caller's <see cref="AppDbContext"/> (no SaveChanges),
/// so they commit atomically with the comment change.
/// </summary>
public class CommentNotifier(IEmailQueue emailQueue, WebhookDispatcher webhooks)
{
    public const string EventCreated = "comment.created";
    public const string EventUpdated = "comment.updated";
    public const string EventDeleted = "comment.deleted";
    public const string EventApproved = "comment.approved";
    public const string EventRejected = "comment.rejected";

    public static readonly IReadOnlyList<string> Events =
        [EventCreated, EventUpdated, EventDeleted, EventApproved, EventRejected];

    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Webhook payload. Excludes the author's email, IP hash and user agent.</summary>
    public static JsonElement Payload(Comment c) => JsonSerializer.SerializeToElement(new
    {
        c.Id,
        c.PageKey,
        c.PageUrl,
        c.PageTitle,
        c.ParentId,
        c.AppUserId,
        c.AuthorName,
        Body = c.IsDeleted ? null : c.Body,
        Status = c.Status.ToApi(),
        c.IsDeleted,
        c.CreatedAt,
        c.EditedAt,
        c.ModeratedAt
    }, SnakeCase);

    public Task StageEventAsync(AppDbContext db, Guid appId, string eventType, Comment comment)
        => webhooks.StageAsync(db, appId, eventType, Payload(comment));

    /// <summary>"comments.new" email to each NotifyEmails address plus the comment.created webhook.</summary>
    public async Task StageCreatedAsync(AppDbContext db, App app, CommentSettings settings, Comment comment)
    {
        var where = comment.PageTitle ?? comment.PageKey;
        var link = SafePageUrl(app, comment.PageUrl);
        var statusText = comment.Status == CommentStatus.Pending ? "awaiting moderation" : "published";
        var text = $"{comment.AuthorName} commented on {where} ({statusText}):\n\n{comment.Body}\n"
                   + (link is null ? "" : $"\n{link}\n");
        var html = $"<p><strong>{Enc(comment.AuthorName)}</strong> commented on {Enc(where)} ({statusText}):</p>"
                   + $"<blockquote style=\"white-space:pre-wrap\">{Enc(comment.Body)}</blockquote>"
                   + (link is null ? "" : $"<p><a href=\"{Enc(link)}\">{Enc(link)}</a></p>");
        foreach (var to in settings.NotifyEmails.Where(EmailQueue.IsValidAddress).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            emailQueue.Stage(db, app.Id, new EmailMessage(to, $"New comment on {where}", text, html,
                ReplyTo: null, Category: "comments.new"));
        }
        await StageEventAsync(db, app.Id, EventCreated, comment);
    }

    /// <summary>
    /// "comments.reply" email to the parent's author when <paramref name="reply"/> is Approved and the parent
    /// was written by a different signed-in AppUser with a valid email.
    /// </summary>
    public async Task StageReplyEmailAsync(AppDbContext db, App app, Comment reply, Comment parent)
    {
        if (reply.Status != CommentStatus.Approved || parent.AppUserId is null || parent.IsDeleted) return;
        if (reply.AppUserId == parent.AppUserId) return;
        var email = await db.AppUsers
            .Where(u => u.AppId == app.Id && u.Id == parent.AppUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
        if (!EmailQueue.IsValidAddress(email)) return;

        var where = reply.PageTitle ?? parent.PageTitle ?? reply.PageKey;
        var link = SafePageUrl(app, reply.PageUrl) ?? SafePageUrl(app, parent.PageUrl);
        var text = $"{reply.AuthorName} replied to your comment on {where}:\n\n{reply.Body}\n"
                   + (link is null ? "" : $"\n{link}\n");
        var html = $"<p><strong>{Enc(reply.AuthorName)}</strong> replied to your comment on {Enc(where)}:</p>"
                   + $"<blockquote style=\"white-space:pre-wrap\">{Enc(reply.Body)}</blockquote>"
                   + (link is null ? "" : $"<p><a href=\"{Enc(link)}\">{Enc(link)}</a></p>");
        emailQueue.Stage(db, app.Id, new EmailMessage(email!.Trim(), $"New reply on {where}", text, html,
            ReplyTo: null, Category: "comments.reply"));
    }

    /// <summary>
    /// page_url is client-supplied, so it is only put in emails when its origin is one of the App's
    /// AllowedOrigins (prevents using the notification emails for phishing links).
    /// </summary>
    public static string? SafePageUrl(App app, string? pageUrl)
    {
        if (pageUrl is null || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return app.AllowedOrigins.Any(o => string.Equals(o.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri
            : null;
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
