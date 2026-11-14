namespace Extatic.Api.Notifications;

/// <summary>A fully-resolved email ready for the transport.</summary>
public record OutgoingEmail(
    string FromDisplayName,
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody,
    string? ReplyTo,
    string Category);

/// <summary>Email transport. Throw on failure; <see cref="EmailDeliveryWorker"/> handles retries.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
