using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class WebhookService(AppDbContext db, WebhookDispatcher dispatcher)
{
    public async Task<List<Webhook>> GetForAppAsync(Guid appId)
        => await db.Webhooks.Where(w => w.AppId == appId).ToListAsync();

    public async Task<Webhook> GetByIdAsync(Guid appId, Guid webhookId)
    {
        var webhook = await db.Webhooks
            .FirstOrDefaultAsync(w => w.AppId == appId && w.Id == webhookId);
        return webhook ?? throw new NotFoundException($"Webhook {webhookId} not found");
    }

    public async Task<Webhook> CreateAsync(Guid appId, string url, string[] events)
    {
        var secret = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var webhook = new Webhook
        {
            AppId = appId,
            Url = url,
            Secret = secret,
            Events = events,
            IsActive = true
        };
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync();
        return webhook;
    }

    public async Task<Webhook> UpdateAsync(Webhook webhook, string? url, string[]? events, bool? isActive)
    {
        if (url is not null) webhook.Url = url;
        if (events is not null) webhook.Events = events;
        if (isActive is not null) webhook.IsActive = isActive.Value;
        await db.SaveChangesAsync();
        return webhook;
    }

    public async Task DeleteAsync(Webhook webhook)
    {
        db.Webhooks.Remove(webhook);
        await db.SaveChangesAsync();
    }

    public async Task<List<WebhookDeliveryLog>> GetLogsAsync(Guid webhookId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        return await db.WebhookDeliveryLogs
            .Where(l => l.WebhookId == webhookId && l.CreatedAt >= cutoff)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task RetriggerAsync(Guid webhookId, Guid logId)
    {
        var log = await db.WebhookDeliveryLogs
            .Include(l => l.Webhook)
            .FirstOrDefaultAsync(l => l.WebhookId == webhookId && l.Id == logId);
        if (log is null)
            throw new NotFoundException($"Delivery log {logId} not found");

        await dispatcher.EnqueueWorkItemAsync(new WebhookDeliveryWorkItem(
            log.WebhookId, log.Id, log.Webhook.Url, log.Webhook.Secret, log.Payload));
    }
}
