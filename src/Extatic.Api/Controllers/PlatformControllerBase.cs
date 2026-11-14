using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers;

/// <summary>
/// Base for Platform API controllers. App-scoped routes must be <c>api/apps/{app_slug}/...</c> so
/// AppContextMiddleware resolves <see cref="CurrentApp"/>; authorize with an App* policy
/// (AppAnyAccess / AppOwnerOrEditor / AppOwnerOrAdmin / AppOwnerOnly) or PlatformAdmin.
/// </summary>
[ApiController]
public abstract class PlatformControllerBase : ControllerBase
{
    /// <summary>The App resolved from <c>{app_slug}</c>. Throws 404 when the route has no app.</summary>
    protected App CurrentApp => HttpContext.Items["CurrentApp"] as App
        ?? throw new NotFoundException("App not found");

    /// <summary>The platform User's id from the OAuth proxy identity. Throws 401 when absent.</summary>
    protected Guid UserId => Guid.TryParse(User.FindFirst(AppClaimTypes.UserId)?.Value, out var id)
        ? id
        : throw new UnauthorizedException("Platform user not authenticated");

    protected string? UserEmail => User.FindFirst(AppClaimTypes.UserEmail)?.Value;
}
