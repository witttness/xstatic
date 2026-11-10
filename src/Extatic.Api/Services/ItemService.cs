using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class ItemService(AppDbContext db, JsonSchemaValidator validator, WebhookDispatcher webhookDispatcher)
{
    public async Task<(List<Item> Items, int Total)> GetForCollectionAsync(
        Guid collectionId, int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || page - 1 > int.MaxValue / pageSize)
            throw new ArgumentException("page must be at least 1 and page_size must be between 1 and 100");
        var query = db.Items.Where(i => i.CollectionId == collectionId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(i => i.Attachments)
            .ToListAsync();
        return (items, total);
    }

    public async Task<Item> GetByIdAsync(Guid collectionId, Guid itemId)
    {
        var item = await db.Items
            .Include(i => i.Attachments)
            .FirstOrDefaultAsync(i => i.CollectionId == collectionId && i.Id == itemId);
        return item ?? throw new NotFoundException($"Item {itemId} not found");
    }

    public async Task<Item> CreateAsync(Guid collectionId, Guid? appUserId, JsonElement data,
        string? schema, Guid appId)
    {
        if (schema is not null)
        {
            var errors = validator.Validate(schema, data);
            if (errors.Count > 0)
                throw new ValidationException("Item data does not match collection schema", errors);
        }

        var item = new Item
        {
            CollectionId = collectionId,
            AppUserId = appUserId,
            Data = data.GetRawText()
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Items.Add(item);
        await webhookDispatcher.StageAsync(db, appId, "item.created", data);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return item;
    }

    public async Task<Item> UpdateAsync(Item item, JsonElement data, string? schema, Guid appId,
        long? expectedVersion = null)
    {
        if (expectedVersion is not null && item.Version != expectedVersion.Value)
            throw new ConflictException("Item changed since it was opened. Reload before saving.");

        if (schema is not null)
        {
            var errors = validator.Validate(schema, data);
            if (errors.Count > 0)
                throw new ValidationException("Item data does not match collection schema", errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        item.Data = data.GetRawText();
        item.Version++;
        await webhookDispatcher.StageAsync(db, appId, "item.updated", data);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Item changed since it was opened. Reload before saving.");
        }
        return item;
    }

    public async Task DeleteAsync(Item item, AttachmentService attachmentService, Guid appId)
    {
        var itemId = item.Id;
        await attachmentService.DeleteAllForItemAsync(item);
        db.Items.Remove(item);
        await db.SaveChangesAsync();
        await webhookDispatcher.EnqueueAsync(appId, "item.deleted", itemId,
            JsonSerializer.SerializeToElement(new { id = itemId }));
    }
}
