using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class AttachmentService(AppDbContext db, IBlobStorageService blobStorage)
{
    public async Task<List<Attachment>> GetForItemAsync(Guid itemId)
        => await db.Attachments.Where(a => a.ItemId == itemId).ToListAsync();

    public async Task<Attachment> GetByIdAsync(Guid itemId, Guid attachmentId)
    {
        var attachment = await db.Attachments
            .FirstOrDefaultAsync(a => a.ItemId == itemId && a.Id == attachmentId);
        return attachment ?? throw new NotFoundException($"Attachment {attachmentId} not found");
    }

    public async Task<Attachment> UploadAsync(
        Item item,
        Collection collection,
        App app,
        Guid? appUserId,
        string filename,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken ct = default)
    {
        if (!collection.AttachmentsEnabled)
            throw new ForbiddenException("Attachments are not enabled for this collection");

        if (collection.AllowedAttachmentTypes.Length > 0
            && !collection.AllowedAttachmentTypes.Contains(contentType))
            throw new ValidationException(
                $"Content type '{contentType}' is not allowed",
                [new ValidationError("content_type", $"Must be one of: {string.Join(", ", collection.AllowedAttachmentTypes)}")]);

        var maxSizeBytes = (long)app.MaxFileSizeMb * 1024 * 1024;
        if (sizeBytes > maxSizeBytes)
            throw new FileTooLargeException($"File size {sizeBytes} exceeds maximum {maxSizeBytes} bytes");

        var currentCount = await db.Attachments.CountAsync(a => a.ItemId == item.Id, ct);
        if (currentCount >= app.MaxAttachmentsPerItem)
            throw new ValidationException(
                $"Maximum attachment count ({app.MaxAttachmentsPerItem}) reached",
                [new ValidationError("attachments", "Maximum count reached")]);

        var attachmentId = Guid.NewGuid();
        var storagePath = $"{app.Id}/{collection.Id}/{item.Id}/{attachmentId}/{filename}";
        var url = await blobStorage.UploadAsync(storagePath, content, contentType, ct);

        var attachment = new Attachment
        {
            Id = attachmentId,
            ItemId = item.Id,
            AppUserId = appUserId,
            Filename = filename,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StoragePath = storagePath,
            Url = url
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return attachment;
    }

    public async Task DeleteAsync(Attachment attachment)
    {
        await blobStorage.DeleteAsync(attachment.StoragePath);
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAllForItemAsync(Item item)
    {
        var attachments = await db.Attachments.Where(a => a.ItemId == item.Id).ToListAsync();
        foreach (var attachment in attachments)
            await blobStorage.DeleteAsync(attachment.StoragePath);
        db.Attachments.RemoveRange(attachments);
        await db.SaveChangesAsync();
    }
}
