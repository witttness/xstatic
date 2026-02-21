using System.Text.Json.Serialization;

namespace Extatic.Api.Dtos;

public record AppDto(
    Guid Id,
    string Name,
    string Slug,
    string[] AllowedOrigins,
    int MaxFileSizeMb,
    int MaxAttachmentsPerItem,
    int StorageQuotaGb,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateAppRequest(string Name, string Slug);

public record UpdateAppRequest(
    string? Name,
    string[]? AllowedOrigins,
    int? MaxFileSizeMb,
    int? MaxAttachmentsPerItem,
    int? StorageQuotaGb);

public record CreateAppResponse(AppDto App, string ApiKey);
public record RegenerateApiKeyResponse(string ApiKey);
