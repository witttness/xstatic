namespace Extatic.Api.Notifications;

/// <summary>A transactional email to stage with <see cref="IEmailQueue.Stage"/>.</summary>
/// <param name="To">Single bare address (no display name). Validated; invalid → 422.</param>
/// <param name="Subject">CR/LF and control characters are stripped; truncated to 500 chars.</param>
/// <param name="Category">Feature-qualified purpose, e.g. "forms.notification".</param>
public record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody,
    string? ReplyTo,
    string Category);
