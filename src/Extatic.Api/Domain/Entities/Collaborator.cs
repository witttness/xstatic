using Extatic.Api.Domain.Enums;

namespace Extatic.Api.Domain.Entities;

public class Collaborator
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid UserId { get; set; }
    public CollaboratorRole Role { get; set; }
    public Guid? InvitedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public User User { get; set; } = null!;
}
