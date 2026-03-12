using System.Text.Json;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Dtos;

public static class DtoMappers
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Email, u.Name, u.CreatedAt);

    public static AppDto ToDto(this App a) =>
        new(a.Id, a.Name, a.Slug, a.PublicId, a.AllowedOrigins,
            a.MaxFileSizeMb, a.MaxAttachmentsPerItem, a.StorageQuotaGb,
            a.CreatedAt, a.UpdatedAt);

    public static CollectionDto ToDto(this Collection c) =>
        new(c.Id, c.AppId, c.Name, c.Slug, c.Schema,
            c.AttachmentsEnabled, c.AllowedAttachmentTypes, c.CreatedAt, c.UpdatedAt);

    public static ItemDto ToDto(this Item i) =>
        new(i.Id, i.CollectionId, i.AppUserId,
            JsonDocument.Parse(i.Data).RootElement,
            i.CreatedAt, i.UpdatedAt,
            i.Attachments?.Select(a => a.ToDto()).ToList());

    public static AttachmentDto ToDto(this Attachment a) =>
        new(a.Id, a.ItemId, a.AppUserId, a.Filename, a.ContentType, a.SizeBytes, a.Url, a.CreatedAt);

    public static AppUserDto ToDto(this AppUser u) =>
        new(u.Id, u.AppId, u.Provider, u.Email, u.DisplayName, u.AvatarUrl, u.LastLoginAt, u.CreatedAt);

    public static CollaboratorDto ToDto(this Collaborator c) =>
        new(c.Id, c.AppId, c.UserId, c.User?.Email ?? "", c.User?.Name ?? "",
            c.Role, c.AcceptedAt, c.CreatedAt);

    public static WebhookDto ToDto(this Webhook w) =>
        new(w.Id, w.AppId, w.Url, w.Events, w.IsActive, w.CreatedAt, w.UpdatedAt);

    public static WebhookDeliveryLogDto ToDto(this WebhookDeliveryLog l) =>
        new(l.Id, l.WebhookId, l.EventType,
            JsonDocument.Parse(l.Payload).RootElement,
            l.StatusCode, l.ResponseBody, l.AttemptNumber,
            l.NextRetryAt, l.SucceededAt, l.CreatedAt);
}
