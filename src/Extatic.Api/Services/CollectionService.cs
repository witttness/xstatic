using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.SchemaLibrary;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class CollectionService(AppDbContext db)
{
    public async Task<List<Collection>> GetForAppAsync(Guid appId)
        => await db.Collections.Where(c => c.AppId == appId).ToListAsync();

    public async Task<Collection> GetBySlugAsync(Guid appId, string slug)
    {
        var collection = await db.Collections
            .FirstOrDefaultAsync(c => c.AppId == appId && c.Slug == slug);
        return collection ?? throw new NotFoundException($"Collection '{slug}' not found");
    }

    public async Task<Collection> GetByIdAsync(Guid id)
    {
        var collection = await db.Collections.FindAsync(id);
        return collection ?? throw new NotFoundException($"Collection {id} not found");
    }

    public async Task<Collection> CreateAsync(Guid appId, string name, string slug,
        string? schema, bool attachmentsEnabled, string[] allowedAttachmentTypes,
        Guid? sourceSchemaId = null, int? sourceSchemaVersion = null, bool callerIsPlatformAdmin = false)
    {
        if (await db.Collections.AnyAsync(c => c.AppId == appId && c.Slug == slug))
            throw new ConflictException($"Collection slug '{slug}' already exists in this app");

        (Guid Id, int Version)? provenance = null;
        if (sourceSchemaId is not null)
            provenance = await LibrarySchemaAccess.VerifyProvenanceAsync(
                db, appId, callerIsPlatformAdmin, sourceSchemaId.Value, sourceSchemaVersion);
        else if (sourceSchemaVersion is not null)
            throw SourceVersionWithoutId();

        var collection = new Collection
        {
            AppId = appId,
            Name = name,
            Slug = slug,
            Schema = schema,
            AttachmentsEnabled = attachmentsEnabled,
            AllowedAttachmentTypes = allowedAttachmentTypes
        };
        if (provenance is not { } source)
        {
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            return collection;
        }

        // Provenance columns are excluded from INSERT (see CollectionConfiguration): write them with an
        // UPDATE in the same transaction.
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        collection.SourceSchemaId = source.Id;
        collection.SourceSchemaVersion = source.Version;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return collection;
    }

    public async Task<Collection> UpdateAsync(Collection collection, string? name, string? schema,
        bool? attachmentsEnabled, string[]? allowedAttachmentTypes,
        Guid? sourceSchemaId = null, int? sourceSchemaVersion = null, bool detachSourceSchema = false,
        bool callerIsPlatformAdmin = false)
    {
        if (detachSourceSchema && (sourceSchemaId is not null || sourceSchemaVersion is not null))
            throw new ValidationException("Conflicting provenance",
                [new ValidationError("detach_source_schema", "Cannot detach and set a source schema in one request")]);
        if (sourceSchemaId is null && sourceSchemaVersion is not null) throw SourceVersionWithoutId();

        if (detachSourceSchema)
        {
            collection.SourceSchemaId = null;
            collection.SourceSchemaVersion = null;
        }
        else if (sourceSchemaId is not null
                 && (sourceSchemaId != collection.SourceSchemaId
                     || (sourceSchemaVersion ?? collection.SourceSchemaVersion) != collection.SourceSchemaVersion))
        {
            // Only re-verify when provenance changes, so re-saving a collection whose source was later
            // archived or hidden still works.
            var (id, version) = await LibrarySchemaAccess.VerifyProvenanceAsync(
                db, collection.AppId, callerIsPlatformAdmin, sourceSchemaId.Value, sourceSchemaVersion);
            collection.SourceSchemaId = id;
            collection.SourceSchemaVersion = version;
        }

        if (name is not null) collection.Name = name;
        if (schema is not null) collection.Schema = schema;
        if (attachmentsEnabled is not null) collection.AttachmentsEnabled = attachmentsEnabled.Value;
        if (allowedAttachmentTypes is not null) collection.AllowedAttachmentTypes = allowedAttachmentTypes;
        await db.SaveChangesAsync();
        return collection;
    }

    /// <summary>Name/latest version of each collection's source schema that the caller may still see.</summary>
    public Task<Dictionary<Guid, SourceSchemaInfo>> GetSourceSchemaInfoAsync(Guid appId,
        IEnumerable<Collection> collections, bool callerIsPlatformAdmin)
        => LibrarySchemaAccess.LookupAsync(db, appId, callerIsPlatformAdmin,
            collections.Where(c => c.SourceSchemaId is not null).Select(c => c.SourceSchemaId!.Value));

    private static ValidationException SourceVersionWithoutId() =>
        new("Missing source schema", [new ValidationError("source_schema_id", "Required with source_schema_version")]);

    public async Task DeleteAsync(Collection collection)
    {
        db.Collections.Remove(collection);
        await db.SaveChangesAsync();
    }
}
