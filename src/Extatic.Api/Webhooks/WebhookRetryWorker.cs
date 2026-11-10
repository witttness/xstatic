using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Webhooks;

public class WebhookRetryWorker(
    WebhookDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookRetryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing webhook retries");
            }
        }
    }

    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var dueIds = await db.WebhookDeliveryLogs
            .Where(l => l.SucceededAt == null
                && l.AttemptNumber < 5
                && l.Webhook.IsActive
                && (l.NextRetryAt <= now || l.ClaimedUntil <= now))
            .OrderBy(l => l.NextRetryAt)
            .Select(l => l.Id)
            .Take(100)
            .ToListAsync(ct);

        foreach (var logId in dueIds)
        {
            var claimed = await db.WebhookDeliveryLogs
                .Where(l => l.Id == logId
                    && l.SucceededAt == null
                    && l.AttemptNumber < 5
                    && l.Webhook.IsActive
                    && (l.NextRetryAt <= now || l.ClaimedUntil <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.AttemptNumber, l => l.AttemptNumber + 1)
                    .SetProperty(l => l.NextRetryAt, (DateTime?)null)
                    .SetProperty(l => l.ClaimedUntil, now.AddMinutes(2)), ct);
            if (claimed == 0) continue;

            var log = await db.WebhookDeliveryLogs
                .AsNoTracking()
                .Include(l => l.Webhook)
                .FirstAsync(l => l.Id == logId, ct);

            await dispatcher.EnqueueWorkItemAsync(new WebhookDeliveryWorkItem(
                log.WebhookId, log.Id, log.Webhook.Url, log.Webhook.Secret, log.Payload));
        }
    }
}
