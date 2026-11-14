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
    DateTime UpdatedAt,
    Guid? SourceSchemaId = null,
    int? SourceSchemaVersion = null,
    string? SourceSchemaName = null,
    int? SourceSchemaLatestVersion = null);

public record CreateCollectionRequest(
    string Name,
    string Slug,
    string? Schema,
    bool AttachmentsEnabled = false,
    string[]? AllowedAttachmentTypes = null,
    Guid? SourceSchemaId = null,
    int? SourceSchemaVersion = null);

public record UpdateCollectionRequest(
    string? Name,
    string? Schema,
    bool? AttachmentsEnabled,
    string[]? AllowedAttachmentTypes,
    Guid? SourceSchemaId = null,
    int? SourceSchemaVersion = null,
    bool? DetachSourceSchema = null);
