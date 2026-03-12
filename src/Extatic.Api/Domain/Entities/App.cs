namespace Extatic.Api.Domain.Entities;

public class App
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public string[] AllowedOrigins { get; set; } = [];
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxAttachmentsPerItem { get; set; } = 10;
    public int StorageQuotaGb { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public ICollection<Collection> Collections { get; set; } = [];
    public ICollection<AppUser> AppUsers { get; set; } = [];
    public ICollection<Collaborator> Collaborators { get; set; } = [];
    public ICollection<Webhook> Webhooks { get; set; } = [];
}
