using Extatic.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Extatic.Api.Auth.Requirements;

/// <summary>
/// Requirement that specifies the minimum collaborator role required to access
/// an app-scoped resource, or whether only the app owner is allowed.
/// </summary>
/// <remarks>
/// Instances of this requirement are evaluated by <see cref="Extatic.Api.Auth.Handlers.AppRoleHandler"/>.
/// - If <see cref="OwnerOnly"/> is <c>true</c> the requirement only succeeds for the app owner.
/// - If <see cref="MinimumRole"/> is provided, a collaborator must have a role greater than
///   or equal to that value and have accepted the invitation.
/// </remarks>
public class AppRoleRequirement(CollaboratorRole? minimumRole, bool ownerOnly = false)
    : IAuthorizationRequirement
{
    /// <summary>
    /// The minimum <see cref="CollaboratorRole"/> required to satisfy this requirement.
    /// When <c>null</c> any accepted collaborator satisfies the requirement (unless
    /// <see cref="OwnerOnly"/> is <c>true</c>).
    /// </summary>
    public CollaboratorRole? MinimumRole { get; } = minimumRole;

    /// <summary>
    /// When <c>true</c> only the app owner (ownerId) satisfies the requirement.
    /// </summary>
    public bool OwnerOnly { get; } = ownerOnly;
}
