using Extatic.Api.Domain.Enums;

namespace Extatic.Api.Dtos;

public record CollaboratorDto(
    Guid Id,
    Guid AppId,
    Guid UserId,
    string UserEmail,
    string UserName,
    CollaboratorRole Role,
    DateTime? AcceptedAt,
    DateTime CreatedAt);

public record InviteCollaboratorRequest(string Email, CollaboratorRole Role);
public record UpdateCollaboratorRoleRequest(CollaboratorRole Role);
