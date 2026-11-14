using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Members;

/// <summary>Client API for static sites: plans, the signed-in member's own membership, directory.</summary>
[Route("api/{app_public_id}/members")]
public class ClientMembersController(MembershipService service) : ClientControllerBase
{
    [HttpGet("plans")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<List<ClientPlanDto>>> Plans()
        => Ok(await service.ListClientPlansAsync(CurrentApp.Id));

    [HttpGet("me")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<ClientMeResponse>> Me()
    {
        var m = await service.GetLatestForUserAsync(CurrentApp.Id, RequireAppUserId());
        return Ok(new ClientMeResponse(m?.ToClientDto()));
    }

    [HttpGet("me/access")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<ClientAccessResponse>> Access([FromQuery] string? plan = null)
        => Ok(new ClientAccessResponse(await service.IsActiveMemberAsync(CurrentApp.Id, RequireAppUserId(), plan)));

    [HttpPost("me")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<ClientMeResponse>> Join([FromBody] JoinMembershipRequest req)
    {
        var m = await service.JoinAsync(CurrentApp.Id, RequireAppUserId(), req);
        return StatusCode(StatusCodes.Status201Created, new ClientMeResponse(m.ToClientDto()));
    }

    [HttpPut("me/profile")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<ClientMeResponse>> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var m = await service.UpdateProfileAsync(CurrentApp.Id, RequireAppUserId(), req.Profile);
        return Ok(new ClientMeResponse(m.ToClientDto()));
    }

    [HttpPut("me/directory")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<ClientMeResponse>> UpdateDirectory([FromBody] UpdateDirectoryRequest req)
    {
        var m = await service.SetDirectoryOptInAsync(CurrentApp.Id, RequireAppUserId(), req.ShowInDirectory);
        return Ok(new ClientMeResponse(m.ToClientDto()));
    }

    [HttpPost("me/cancel")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<ClientMeResponse>> Cancel()
    {
        var m = await service.MemberCancelAsync(CurrentApp.Id, RequireAppUserId());
        return Ok(new ClientMeResponse(m.ToClientDto()));
    }

    [HttpGet("directory")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PagedResult<DirectoryEntryDto>>> Directory(
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(await service.GetDirectoryAsync(CurrentApp.Id, RequireAppUserId(), page, page_size));
}
