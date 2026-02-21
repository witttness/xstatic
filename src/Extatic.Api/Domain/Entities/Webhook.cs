namespace Extatic.Api.Domain.Entities;

public class Webhook
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string[] Events { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<WebhookDeliveryLog> DeliveryLogs { get; set; } = [];
}
