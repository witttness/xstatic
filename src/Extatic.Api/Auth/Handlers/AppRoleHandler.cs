using Extatic.Api.Auth.Requirements;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Auth.Handlers;

/// <summary>
/// Authorization handler that enforces app-level roles for Platform users.
/// </summary>
/// <remarks>
/// This handler checks the current HTTP context for a `CurrentApp` instance
/// (stored in `HttpContext.Items`) and validates whether the current user is
/// allowed to perform the action described by the <see cref="AppRoleRequirement"/>.
/// Behavior:
/// - If the user is the app owner (`App.OwnerId`) the requirement always succeeds.
/// - If the requirement is marked <c>OwnerOnly</c> and the user is not the owner,
///   the requirement fails.
/// - Otherwise the handler looks up an accepted <c>Collaborator</c> record and
///   ensures the collaborator's role meets the optional <c>MinimumRole</c>.
/// </remarks>
public class AppRoleHandler(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<AppRoleRequirement>
{
    /// <summary>
    /// Handles evaluation of the <see cref="AppRoleRequirement"/> for the current
    /// authorization context.
    /// </summary>
    /// <param name="context">The current <see cref="AuthorizationHandlerContext"/>.</param>
    /// <param name="requirement">The <see cref="AppRoleRequirement"/> to evaluate.
    /// If <c>OwnerOnly</c> is true the requirement only succeeds for the app owner.
    /// If <c>MinimumRole</c> is set, a collaborator must have a role greater than or
    /// equal to that value and have accepted the invitation.</param>
    /// <returns>A <see cref="Task"/> that completes when evaluation finishes.</returns>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppRoleRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        var userIdClaim = context.User.FindFirst(AppClaimTypes.UserId);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId)) return;

        var app = httpContext.Items["CurrentApp"] as App;
        if (app is null) return;

        // Owner always has access
        if (app.OwnerId == userId)
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.OwnerOnly) return;

        // Check collaborator role
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collaborator = await db.Collaborators
            .FirstOrDefaultAsync(c => c.AppId == app.Id && c.UserId == userId && c.AcceptedAt != null);

        if (collaborator is null) return;

        if (requirement.MinimumRole is null || collaborator.Role >= requirement.MinimumRole.Value)
        {
            context.Succeed(requirement);
        }
    }
}
