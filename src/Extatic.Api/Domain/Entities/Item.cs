namespace Extatic.Api.Domain.Entities;

public class Item
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public Guid? AppUserId { get; set; }
    public string Data { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Collection Collection { get; set; } = null!;
    public AppUser? AppUser { get; set; }
    public ICollection<Attachment> Attachments { get; set; } = [];
}
