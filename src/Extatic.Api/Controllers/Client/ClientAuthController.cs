using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Controllers.Client;

[ApiController]
[Route("api/{app_public_id}/auth")]
[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
public class ClientAuthController(DevTokenService devTokenService, AppUserService appUserService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpPost("dev/token")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<DevTokenResponse>> GetDevToken([FromBody] DevTokenRequest req)
    {
        var (user, token) = await devTokenService.IssueAsync(
            appUserService, CurrentApp.Id, req.Email, req.DisplayName);
        return Ok(new DevTokenResponse(user.ToDto(), token));
    }
}
