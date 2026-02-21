using Extatic.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Extatic.Api.Auth.Requirements;

public class AppRoleRequirement(CollaboratorRole? minimumRole, bool ownerOnly = false)
    : IAuthorizationRequirement
{
    public CollaboratorRole? MinimumRole { get; } = minimumRole;
    public bool OwnerOnly { get; } = ownerOnly;
}
