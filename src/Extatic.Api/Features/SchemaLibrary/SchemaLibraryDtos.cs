using System.Text.Json;
using Extatic.Api.Services;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>List row (no content). <c>Visibility</c> is null for App schemas.</summary>
public record LibrarySchemaSummaryDto(
    Guid Id,
    string Scope,
    Guid? AppId,
    string Name,
    string Slug,
    string? Description,
    string? Category,
    string[] Tags,
    string? Visibility,
    int Version,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record LibrarySchemaVersionSummaryDto(int Version, string? ChangeNote, Guid CreatedByUserId, DateTime CreatedAt);

public record LibrarySchemaDto(
    Guid Id,
    string Scope,
    Guid? AppId,
    string Name,
    string Slug,
    string? Description,
    string? Category,
    string[] Tags,
    string? Visibility,
    int Version,
    bool IsArchived,
    JsonElement Schema,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<LibrarySchemaVersionSummaryDto> Versions);

public record LibrarySchemaVersionDto(
    Guid LibrarySchemaId,
    int Version,
    JsonElement Schema,
    string? ChangeNote,
    Guid CreatedByUserId,
    DateTime CreatedAt);

/// <summary>Picker row. <c>AdminOnly</c> flags global schemas only platform admins can see.</summary>
public record AvailableSchemaDto(
    Guid Id,
    string Scope,
    string Name,
    string Slug,
    string? Description,
    string? Category,
    string[] Tags,
    int Version,
    bool AdminOnly);

public record AvailableSchemaDetailDto(
    Guid Id,
    string Scope,
    string Name,
    string Slug,
    string? Description,
    string? Category,
    string[] Tags,
    int Version,
    bool AdminOnly,
    JsonElement Schema);

public record SchemaUsageDto(
    Guid AppId,
    string AppSlug,
    string AppName,
    Guid CollectionId,
    string CollectionSlug,
    string CollectionName,
    int? SourceSchemaVersion);

public record CreateLibrarySchemaRequest(
    string Name,
    string Slug,
    JsonElement Schema,
    string? Description = null,
    string? Category = null,
    string[]? Tags = null,
    string? Visibility = null,
    string? ChangeNote = null);

/// <summary>Null fields are left unchanged; an empty description/category clears it.</summary>
public record UpdateLibrarySchemaRequest(
    string? Name = null,
    string? Slug = null,
    JsonElement? Schema = null,
    string? Description = null,
    string? Category = null,
    string[]? Tags = null,
    string? ChangeNote = null);

public record ChangeVisibilityRequest(string Visibility);

public record ValidateSchemaRequest(JsonElement Schema, JsonElement? Sample = null);

/// <summary><c>SampleErrors</c> is null when no sample was given (or the schema itself is invalid).</summary>
public record ValidateSchemaResponse(bool Valid, List<ValidationError> SchemaErrors, List<ValidationError>? SampleErrors);

public static class SchemaLibraryMappers
{
    public static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static LibrarySchemaSummaryDto ToSummaryDto(this LibrarySchema s) =>
        new(s.Id, s.Scope.ToWire(), s.AppId, s.Name, s.Slug, s.Description, s.Category, s.Tags,
            s.Visibility?.ToWire(), s.Version, s.IsArchived, s.CreatedAt, s.UpdatedAt);

    public static LibrarySchemaDto ToDto(this LibrarySchema s, IEnumerable<LibrarySchemaVersion> versions) =>
        new(s.Id, s.Scope.ToWire(), s.AppId, s.Name, s.Slug, s.Description, s.Category, s.Tags,
            s.Visibility?.ToWire(), s.Version, s.IsArchived, ParseJson(s.Schema),
            s.CreatedByUserId, s.UpdatedByUserId, s.CreatedAt, s.UpdatedAt,
            versions.OrderByDescending(v => v.Version)
                .Select(v => new LibrarySchemaVersionSummaryDto(v.Version, v.ChangeNote, v.CreatedByUserId, v.CreatedAt))
                .ToList());

    public static LibrarySchemaVersionDto ToDto(this LibrarySchemaVersion v) =>
        new(v.LibrarySchemaId, v.Version, ParseJson(v.Schema), v.ChangeNote, v.CreatedByUserId, v.CreatedAt);

    public static AvailableSchemaDto ToAvailableDto(this LibrarySchema s) =>
        new(s.Id, s.Scope.ToWire(), s.Name, s.Slug, s.Description, s.Category, s.Tags, s.Version,
            s.Visibility == LibrarySchemaVisibility.AdminOnly);

    public static AvailableSchemaDetailDto ToAvailableDetailDto(this LibrarySchema s) =>
        new(s.Id, s.Scope.ToWire(), s.Name, s.Slug, s.Description, s.Category, s.Tags, s.Version,
            s.Visibility == LibrarySchemaVisibility.AdminOnly, ParseJson(s.Schema));
}
