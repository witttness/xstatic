using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Authentication;

[Route("api/apps/{app_slug}/auth")]
[Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
public class PlatformAuthController(AuthSettingsService settingsService, PasswordAuthService auth)
    : PlatformControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<AuthSettingsDto>> GetSettings()
    {
        var (settings, persisted) = await settingsService.GetAsync(CurrentApp.Id);
        return Ok(settings.ToDto(persisted));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<AuthSettingsDto>> UpdateSettings([FromBody] UpdateAuthSettingsRequest req)
        => Ok((await settingsService.UpdateAsync(CurrentApp, req)).ToDto());

    [HttpPost("users/{id:guid}/disable")]
    public async Task<ActionResult<AppUserDto>> Disable(Guid id)
        => Ok((await auth.SetDisabledAsync(CurrentApp.Id, id, disabled: true)).ToDto());

    [HttpPost("users/{id:guid}/enable")]
    public async Task<ActionResult<AppUserDto>> Enable(Guid id)
        => Ok((await auth.SetDisabledAsync(CurrentApp.Id, id, disabled: false)).ToDto());

    [HttpPost("users/{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(Guid id)
    {
        var user = await auth.GetUserAsync(CurrentApp.Id, id);
        await auth.RevokeAllSessionsAsync(CurrentApp.Id, user.Id);
        return NoContent();
    }

    [HttpPost("users/{id:guid}/send-password-reset")]
    public async Task<IActionResult> SendPasswordReset(Guid id)
    {
        await auth.SendPasswordResetAsync(CurrentApp, id, ClientIps.Hash(HttpContext));
        return Accepted();
    }
}
