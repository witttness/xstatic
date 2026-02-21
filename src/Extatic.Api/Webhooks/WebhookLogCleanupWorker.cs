using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Webhooks;

public class WebhookLogCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookLogCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-7);
                var deleted = await db.WebhookDeliveryLogs
                    .Where(l => l.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                logger.LogInformation("Cleaned up {Count} webhook delivery logs", deleted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during webhook log cleanup");
            }
        }
    }
}
