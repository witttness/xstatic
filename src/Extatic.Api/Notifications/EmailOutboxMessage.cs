using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Notifications;

public enum EmailOutboxStatus
{
    Pending,
    Sent,
    Failed
}

/// <summary>
/// A transactional email waiting to be (or already) delivered by <see cref="EmailDeliveryWorker"/>.
/// Rows are written via <see cref="IEmailQueue.Stage"/> in the same SaveChanges as the business change.
/// </summary>
public class EmailOutboxMessage : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string? ReplyTo { get; set; }
    /// <summary>Feature-qualified purpose, e.g. "forms.notification". Used for logging/metrics.</summary>
    public string Category { get; set; } = string.Empty;
    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;
    public int Attempts { get; set; }
    /// <summary>When the row is next eligible for delivery. Also acts as the claim lease while sending.</summary>
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public App App { get; set; } = null!;
}
