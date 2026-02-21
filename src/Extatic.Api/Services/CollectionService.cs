using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
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
        string? schema, bool attachmentsEnabled, string[] allowedAttachmentTypes)
    {
        if (await db.Collections.AnyAsync(c => c.AppId == appId && c.Slug == slug))
            throw new ConflictException($"Collection slug '{slug}' already exists in this app");

        var collection = new Collection
        {
            AppId = appId,
            Name = name,
            Slug = slug,
            Schema = schema,
            AttachmentsEnabled = attachmentsEnabled,
            AllowedAttachmentTypes = allowedAttachmentTypes
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        return collection;
    }

    public async Task<Collection> UpdateAsync(Collection collection, string? name, string? schema,
        bool? attachmentsEnabled, string[]? allowedAttachmentTypes)
    {
        if (name is not null) collection.Name = name;
        if (schema is not null) collection.Schema = schema;
        if (attachmentsEnabled is not null) collection.AttachmentsEnabled = attachmentsEnabled.Value;
        if (allowedAttachmentTypes is not null) collection.AllowedAttachmentTypes = allowedAttachmentTypes;
        await db.SaveChangesAsync();
        return collection;
    }

    public async Task DeleteAsync(Collection collection)
    {
        db.Collections.Remove(collection);
        await db.SaveChangesAsync();
    }
}
