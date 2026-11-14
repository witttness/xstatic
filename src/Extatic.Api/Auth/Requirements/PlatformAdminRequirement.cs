using Microsoft.AspNetCore.Authorization;

namespace Extatic.Api.Auth.Requirements;

public sealed class PlatformAdminRequirement : IAuthorizationRequirement;

public sealed class PlatformAdminHandler(IPlatformAdminService admins)
    : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        if (admins.IsAdmin(context.User.FindFirst(AppClaimTypes.UserEmail)?.Value))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
