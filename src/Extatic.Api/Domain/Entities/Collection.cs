namespace Extatic.Api.Domain.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public bool AttachmentsEnabled { get; set; }
    public string[] AllowedAttachmentTypes { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = [];
}
