namespace Extatic.Api.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<App> OwnedApps { get; set; } = [];
    public ICollection<Collaborator> Collaborations { get; set; } = [];
}
