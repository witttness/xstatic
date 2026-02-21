namespace Extatic.Api.Webhooks;

public record WebhookDeliveryWorkItem(
    Guid WebhookId,
    Guid LogId,
    string Url,
    string Secret,
    string Payload);
