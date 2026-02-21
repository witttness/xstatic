using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Webhooks;

public class WebhookDeliveryWorker(
    WebhookDispatcher dispatcher,
    WebhookPayloadBuilder payloadBuilder,
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliveryWorker> logger,
    IHttpClientFactory httpClientFactory)
    : BackgroundService
{
    private static readonly int[] RetryDelaysMinutes = [1, 5, 30, 120, 720];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            _ = ProcessAsync(workItem, stoppingToken);
        }
    }

    private async Task ProcessAsync(WebhookDeliveryWorkItem item, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var log = await db.WebhookDeliveryLogs
            .Include(l => l.Webhook)
            .FirstOrDefaultAsync(l => l.Id == item.LogId, ct);
        if (log is null) return;

        var client = httpClientFactory.CreateClient("webhook");
        var signature = payloadBuilder.ComputeSignature(item.Secret, item.Payload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, item.Url);
            request.Content = new StringContent(item.Payload, System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("X-Extatic-Signature", signature);

            using var response = await client.SendAsync(request, ct);
            log.StatusCode = (int)response.StatusCode;
            log.ResponseBody = (await response.Content.ReadAsStringAsync(ct))[..Math.Min(4096,
                (await response.Content.ReadAsStringAsync(ct)).Length)];

            if (response.IsSuccessStatusCode)
            {
                log.SucceededAt = DateTime.UtcNow;
            }
            else
            {
                await ScheduleRetryOrDeactivate(db, log, log.Webhook);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook delivery failed for {LogId}", item.LogId);
            log.StatusCode = null;
            log.ResponseBody = ex.Message[..Math.Min(4096, ex.Message.Length)];
            await ScheduleRetryOrDeactivate(db, log, log.Webhook);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task ScheduleRetryOrDeactivate(AppDbContext db,
        Domain.Entities.WebhookDeliveryLog log,
        Domain.Entities.Webhook webhook)
    {
        if (log.AttemptNumber >= 5)
        {
            webhook.IsActive = false;
        }
        else
        {
            var delayMinutes = RetryDelaysMinutes[log.AttemptNumber - 1];
            log.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
        }
        await Task.CompletedTask;
    }
}
