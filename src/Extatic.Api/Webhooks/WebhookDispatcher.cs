using System.Text.Json;
using System.Threading.Channels;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Webhooks;

public class WebhookDispatcher(
    IServiceScopeFactory scopeFactory,
    WebhookPayloadBuilder payloadBuilder)
{
    private readonly Channel<WebhookDeliveryWorkItem> _channel =
        Channel.CreateUnbounded<WebhookDeliveryWorkItem>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ChannelReader<WebhookDeliveryWorkItem> Reader => _channel.Reader;

    public async Task StageAsync(AppDbContext db, Guid appId, string eventType, JsonElement data)
    {
        var webhooks = await db.Webhooks
            .Where(w => w.AppId == appId && w.IsActive && w.Events.Contains(eventType))
            .ToListAsync();

        var payload = payloadBuilder.Build(eventType, appId, data);
        foreach (var webhook in webhooks)
        {
            var log = new WebhookDeliveryLog
            {
                WebhookId = webhook.Id,
                EventType = eventType,
                Payload = payload,
                AttemptNumber = 0,
                NextRetryAt = DateTime.UtcNow
            };
            db.WebhookDeliveryLogs.Add(log);
        }
    }

    public async Task EnqueueAsync(Guid appId, string eventType, Guid entityId, JsonElement data)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StageAsync(db, appId, eventType, data);
        await db.SaveChangesAsync();
    }

    public async Task EnqueueWorkItemAsync(WebhookDeliveryWorkItem item)
    {
        await _channel.Writer.WriteAsync(item);
    }
}
