using Extatic.Api.Data;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>Name and current version of a Collection's source schema, as visible to the caller.</summary>
public record SourceSchemaInfo(string Name, int LatestVersion);

/// <summary>
/// The single definition of "which library schemas can this App (and caller) see": Shared global
/// schemas, AdminOnly global schemas for platform admins only, and the App's own schemas. Another
/// App's schemas are never visible.
/// </summary>
public static class LibrarySchemaAccess
{
    public static IQueryable<LibrarySchema> VisibleTo(this IQueryable<LibrarySchema> query, Guid appId,
        bool isPlatformAdmin) =>
        query.Where(s =>
            (s.Scope == LibrarySchemaScope.Global && s.AppId == null
                && (isPlatformAdmin || s.Visibility == LibrarySchemaVisibility.Shared))
            || (s.Scope == LibrarySchemaScope.App && s.AppId == appId));

    /// <summary>
    /// Validates provenance a caller wants to record on a Collection: the schema must be visible to
    /// the caller, not archived, and <paramref name="version"/> (default: current) must exist.
    /// Unknown and invisible schemas get the same 422 so nothing leaks.
    /// </summary>
    public static async Task<(Guid Id, int Version)> VerifyProvenanceAsync(AppDbContext db, Guid appId,
        bool isPlatformAdmin, Guid schemaId, int? version)
    {
        var schema = await db.LibrarySchemas.AsNoTracking().VisibleTo(appId, isPlatformAdmin)
            .FirstOrDefaultAsync(s => s.Id == schemaId);
        if (schema is null)
            throw new ValidationException("Unknown library schema",
                [new ValidationError("source_schema_id", "Library schema not found")]);
        if (schema.IsArchived)
            throw new ValidationException("Archived library schema",
                [new ValidationError("source_schema_id", "This library schema is archived")]);

        var resolved = version ?? schema.Version;
        var exists = await db.LibrarySchemaVersions.AnyAsync(v => v.LibrarySchemaId == schemaId && v.Version == resolved);
        if (!exists)
            throw new ValidationException("Unknown library schema version",
                [new ValidationError("source_schema_version", $"Version {resolved} does not exist")]);
        return (schema.Id, resolved);
    }

    /// <summary>Name/latest version for each visible id (archived included); invisible or deleted ids are absent.</summary>
    public static async Task<Dictionary<Guid, SourceSchemaInfo>> LookupAsync(AppDbContext db, Guid appId,
        bool isPlatformAdmin, IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return [];
        return await db.LibrarySchemas.AsNoTracking().VisibleTo(appId, isPlatformAdmin)
            .Where(s => idList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new SourceSchemaInfo(s.Name, s.Version));
    }
}
