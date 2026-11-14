using System.Net.Mail;
using Extatic.Api.Data;
using Extatic.Api.Services;

namespace Extatic.Api.Notifications;

public class EmailQueue : IEmailQueue
{
    public const int MaxSubjectLength = 500;
    public const int MaxAddressLength = 320;

    public EmailOutboxMessage Stage(AppDbContext db, Guid appId, EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var errors = new List<ValidationError>();
        var to = NormaliseAddress(message.To, "to", errors);
        var replyTo = string.IsNullOrWhiteSpace(message.ReplyTo)
            ? null
            : NormaliseAddress(message.ReplyTo, "reply_to", errors);
        if (string.IsNullOrWhiteSpace(message.Category) || message.Category.Length > 100)
            errors.Add(new ValidationError("category", "Category is required (max 100 characters)"));
        if (errors.Count > 0)
            throw new ValidationException("Invalid email message", errors);

        var outbox = new EmailOutboxMessage
        {
            AppId = appId,
            ToAddress = to!,
            ReplyTo = replyTo,
            Subject = SanitiseSubject(message.Subject),
            TextBody = message.TextBody ?? string.Empty,
            HtmlBody = message.HtmlBody,
            Category = message.Category.Trim(),
            Status = EmailOutboxStatus.Pending,
            NextAttemptAt = DateTime.UtcNow
        };
        db.EmailOutbox.Add(outbox);
        return outbox;
    }

    /// <summary>Removes CR/LF (header injection) and other control characters, collapses to one line.</summary>
    public static string SanitiseSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return string.Empty;
        var chars = subject.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var clean = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > MaxSubjectLength ? clean[..MaxSubjectLength] : clean;
    }

    public static bool IsValidAddress(string? address) => TryNormalise(address, out _);

    private static string? NormaliseAddress(string? address, string pointer, List<ValidationError> errors)
    {
        if (TryNormalise(address, out var normalised)) return normalised;
        errors.Add(new ValidationError(pointer, "Must be a valid email address"));
        return null;
    }

    private static bool TryNormalise(string? address, out string normalised)
    {
        normalised = string.Empty;
        if (string.IsNullOrWhiteSpace(address)) return false;
        var trimmed = address.Trim();
        if (trimmed.Length > MaxAddressLength || trimmed.Any(char.IsControl) || trimmed.Contains(' '))
            return false;
        // Bare addresses only: reject display-name forms ("Name <a@b>") and anything MailAddress rewrites.
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed
            || !string.IsNullOrEmpty(parsed.DisplayName) || !parsed.Host.Contains('.'))
            return false;
        normalised = trimmed;
        return true;
    }
}
