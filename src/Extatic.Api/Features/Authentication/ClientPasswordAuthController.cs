using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Authentication;

/// <summary>End-user email/password auth for static sites. Shares the <c>auth</c> prefix with <c>auth/dev/token</c>.</summary>
[Route("api/{app_public_id}/auth")]
public class ClientPasswordAuthController(PasswordAuthService auth) : ClientControllerBase
{
    private string IpHash => ClientIps.Hash(HttpContext);

    [HttpPost("register")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<AcceptedResponse>> Register([FromBody] RegisterRequest req)
    {
        await auth.RegisterAsync(CurrentApp, req, IpHash);
        return Accepted(new AcceptedResponse(PasswordAuthService.GenericAccepted));
    }

    [HttpPost("verify-email")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<EmailVerifiedResponse>> VerifyEmail([FromBody] TokenRequest req)
    {
        await auth.VerifyEmailAsync(CurrentApp, req.Token);
        return Ok(new EmailVerifiedResponse(true));
    }

    [HttpPost("resend-verification")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<AcceptedResponse>> ResendVerification([FromBody] EmailRequest req)
    {
        await auth.ResendVerificationAsync(CurrentApp, req.Email, IpHash);
        return Accepted(new AcceptedResponse(PasswordAuthService.GenericAccepted));
    }

    [HttpPost("login")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<AuthTokensResponse>> Login([FromBody] LoginRequest req)
    {
        try
        {
            return Ok(await auth.LoginAsync(CurrentApp, req, IpHash));
        }
        catch (EmailNotVerifiedException ex)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = EmailNotVerifiedException.Code;
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    [HttpPost("refresh")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<AuthTokensResponse>> Refresh([FromBody] RefreshRequest req)
        => Ok(await auth.RefreshAsync(CurrentApp, req.RefreshToken, IpHash));

    [HttpPost("logout")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        await auth.LogoutAsync(CurrentApp, req.RefreshToken);
        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<IActionResult> LogoutAll()
    {
        await auth.RevokeAllSessionsAsync(CurrentApp.Id, RequireAppUserId());
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<AcceptedResponse>> ForgotPassword([FromBody] EmailRequest req)
    {
        await auth.ForgotPasswordAsync(CurrentApp, req.Email, IpHash);
        return Accepted(new AcceptedResponse(PasswordAuthService.GenericAccepted));
    }

    [HttpPost("reset-password")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        await auth.ResetPasswordAsync(CurrentApp, req);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<AuthUserDto>> GetMe()
        => Ok((await auth.GetActiveUserAsync(CurrentApp.Id, RequireAppUserId())).ToAuthUserDto());

    [HttpPut("me")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<AuthUserDto>> UpdateMe([FromBody] UpdateProfileRequest req)
        => Ok((await auth.UpdateProfileAsync(CurrentApp.Id, RequireAppUserId(), req)).ToAuthUserDto());

    [HttpPost("change-password")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<AuthTokensResponse>> ChangePassword([FromBody] ChangePasswordRequest req)
        => Ok(await auth.ChangePasswordAsync(CurrentApp, RequireAppUserId(), req, IpHash));

    [HttpDelete("me")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteAccountRequest req)
    {
        await auth.DeleteAccountAsync(CurrentApp, RequireAppUserId(), req.Password);
        return NoContent();
    }
}
