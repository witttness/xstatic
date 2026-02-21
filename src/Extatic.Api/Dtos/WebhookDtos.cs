using System.Text.Json;

namespace Extatic.Api.Dtos;

public record WebhookDto(
    Guid Id,
    Guid AppId,
    string Url,
    string[] Events,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record WebhookDeliveryLogDto(
    Guid Id,
    Guid WebhookId,
    string EventType,
    JsonElement Payload,
    int? StatusCode,
    string? ResponseBody,
    int AttemptNumber,
    DateTime? NextRetryAt,
    DateTime? SucceededAt,
    DateTime CreatedAt);

public record CreateWebhookRequest(string Url, string[] Events);
public record UpdateWebhookRequest(string? Url, string[]? Events, bool? IsActive);
