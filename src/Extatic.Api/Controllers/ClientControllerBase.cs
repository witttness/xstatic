using System.Security.Claims;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers;

/// <summary>
/// Base for Client API controllers (<c>api/{app_public_id}/...</c>). Pair with
/// <c>[Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]</c> (anonymous or signed-in),
/// <c>PolicyNames.AuthenticatedAppUser</c> (signed-in only) or <c>PolicyNames.AppServerKey</c>.
/// </summary>
[ApiController]
public abstract class ClientControllerBase : ControllerBase
{
    /// <summary>The App resolved from the URL by the ApiKey / ServerKey scheme.</summary>
    protected App CurrentApp => HttpContext.Items["CurrentApp"] as App
        ?? throw new UnauthorizedException("App context is not available");

    /// <summary>
    /// The signed-in AppUser's id, or null when anonymous. A token issued for a different App is
    /// treated as anonymous: the app-user id is returned only from an identity whose
    /// <c>extatic:app_id</c> claim equals <see cref="CurrentApp"/>.Id.
    /// </summary>
    protected Guid? AppUserId => ResolveAppUserId(User, HttpContext.Items["CurrentApp"] as App);

    /// <summary>Returns <see cref="AppUserId"/> or throws <see cref="UnauthorizedException"/> (401).</summary>
    [NonAction]
    protected Guid RequireAppUserId()
        => AppUserId ?? throw new UnauthorizedException("Sign-in required");

    internal static Guid? ResolveAppUserId(ClaimsPrincipal? user, App? app)
    {
        if (user is null || app is null) return null;
        foreach (var identity in user.Identities)
        {
            if (!identity.IsAuthenticated) continue;
            if (!Guid.TryParse(identity.FindFirst(AppClaimTypes.AppUserId)?.Value, out var appUserId)) continue;
            if (Guid.TryParse(identity.FindFirst(AppClaimTypes.AppId)?.Value, out var tokenAppId)
                && tokenAppId == app.Id)
                return appUserId;
        }
        return null;
    }
}
