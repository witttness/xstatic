using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Extatic.Api.Auth.Requirements;

public sealed class AppUserAppRequirement : IAuthorizationRequirement;

public sealed class AppUserAppHandler : AuthorizationHandler<AppUserAppRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AppUserAppRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            httpContext.Items["CurrentApp"] is App app &&
            Guid.TryParse(context.User.FindFirst(AppClaimTypes.AppId)?.Value, out var tokenAppId) &&
            Guid.TryParse(context.User.FindFirst(AppClaimTypes.AppUserId)?.Value, out _) &&
            tokenAppId == app.Id)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
