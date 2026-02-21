namespace Extatic.Api.Domain.Entities;

public class Attachment
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid? AppUserId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Item Item { get; set; } = null!;
    public AppUser? AppUser { get; set; }
}
