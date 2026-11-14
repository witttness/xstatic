using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Notifications;

/// <summary>
/// Delivers <see cref="EmailOutboxMessage"/> rows. Safe with multiple replicas: each row is claimed
/// with a conditional UPDATE (same pattern as WebhookRetryWorker) that bumps Attempts and pushes
/// NextAttemptAt out by <see cref="ClaimLease"/>, so a crashed worker's row becomes due again later.
/// </summary>
public class EmailDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IEmailSender sender,
    ILogger<EmailDeliveryWorker> logger)
    : BackgroundService
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(12)
    ];
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SentRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPurge = DateTime.MinValue;
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
                if (DateTime.UtcNow - lastPurge > PurgeInterval)
                {
                    await PurgeSentAsync(stoppingToken);
                    lastPurge = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing email outbox");
            }
        }
    }

    /// <summary>Claims and sends due messages. Returns the number of messages attempted.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var dueIds = await db.EmailOutbox
            .Where(m => m.Status == EmailOutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Select(m => m.Id)
            .Take(50)
            .ToListAsync(ct);

        var attempted = 0;
        foreach (var id in dueIds)
        {
            var leaseUntil = now.Add(ClaimLease);
            var claimed = await db.EmailOutbox
                .Where(m => m.Id == id && m.Status == EmailOutboxStatus.Pending && m.NextAttemptAt <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                    .SetProperty(m => m.NextAttemptAt, leaseUntil), ct);
            if (claimed == 0) continue;

            attempted++;
            var message = await db.EmailOutbox.Include(m => m.App).FirstAsync(m => m.Id == id, ct);
            try
            {
                await sender.SendAsync(new OutgoingEmail(
                    $"{message.App.Name} via Extatic", message.ToAddress, message.Subject,
                    message.TextBody, message.HtmlBody, message.ReplyTo, message.Category), ct);
                message.Status = EmailOutboxStatus.Sent;
                message.SentAt = DateTime.UtcNow;
                message.NextAttemptAt = null;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Email {EmailId} [{Category}] attempt {Attempt} failed: {ErrorType}",
                    message.Id, message.Category, message.Attempts, ex.GetType().Name);
                var error = $"{ex.GetType().Name}: {ex.Message}";
                message.LastError = error.Length > 2000 ? error[..2000] : error;
                if (message.Attempts >= MaxAttempts)
                {
                    message.Status = EmailOutboxStatus.Failed;
                    message.NextAttemptAt = null;
                }
                else
                {
                    message.NextAttemptAt = DateTime.UtcNow.Add(RetryDelays[message.Attempts - 1]);
                }
            }
            await db.SaveChangesAsync(ct);
        }
        return attempted;
    }

    /// <summary>Deletes Sent rows older than <see cref="SentRetention"/>.</summary>
    public async Task<int> PurgeSentAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow - SentRetention;
        return await db.EmailOutbox
            .Where(m => m.Status == EmailOutboxStatus.Sent && m.SentAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
