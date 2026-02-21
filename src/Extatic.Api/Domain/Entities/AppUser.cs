namespace Extatic.Api.Domain.Entities;

public class AppUser
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderUserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Metadata { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
}
