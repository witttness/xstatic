namespace Extatic.Api.Domain.Entities;

public class WebhookDeliveryLog
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? SucceededAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Webhook Webhook { get; set; } = null!;
}
