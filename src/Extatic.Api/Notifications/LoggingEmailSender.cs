namespace Extatic.Api.Notifications;

/// <summary>Used when no SMTP host is configured. Logs metadata only (never bodies).</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        logger.LogInformation("Email (not sent, no SMTP configured) to {To} [{Category}]: {Subject}",
            email.To, email.Category, email.Subject);
        return Task.CompletedTask;
    }
}
