namespace Extatic.Api.Dtos;

public record CollectionDto(
    Guid Id,
    Guid AppId,
    string Name,
    string Slug,
    string? Schema,
    bool AttachmentsEnabled,
    string[] AllowedAttachmentTypes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateCollectionRequest(
    string Name,
    string Slug,
    string? Schema,
    bool AttachmentsEnabled = false,
    string[]? AllowedAttachmentTypes = null);

public record UpdateCollectionRequest(
    string? Name,
    string? Schema,
    bool? AttachmentsEnabled,
    string[]? AllowedAttachmentTypes);
