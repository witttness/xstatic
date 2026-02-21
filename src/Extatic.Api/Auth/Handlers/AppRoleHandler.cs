using Extatic.Api.Auth.Requirements;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Auth.Handlers;

public class AppRoleHandler(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<AppRoleRequirement>
{
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
