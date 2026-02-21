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
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessRetriableLogsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing webhook retries");
            }
        }
    }

    private async Task ProcessRetriableLogsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var logs = await db.WebhookDeliveryLogs
            .Include(l => l.Webhook)
            .Where(l => l.NextRetryAt <= now
                && l.SucceededAt == null
                && l.AttemptNumber < 5
                && l.Webhook.IsActive)
            .ToListAsync(ct);

        foreach (var log in logs)
        {
            log.AttemptNumber++;
            log.NextRetryAt = null;
            await db.SaveChangesAsync(ct);

            await dispatcher.EnqueueWorkItemAsync(new WebhookDeliveryWorkItem(
                log.WebhookId, log.Id, log.Webhook.Url, log.Webhook.Secret, log.Payload));
        }
    }
}
