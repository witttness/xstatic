using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Comments;

/// <summary>Client API for static sites. Never returns author emails.</summary>
[Route("api/{app_public_id}/comments")]
public class ClientCommentsController(CommentService comments) : ClientControllerBase
{
    [HttpGet]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<CommentThreadDto>> List(
        [FromQuery] string? page_key,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20,
        [FromQuery] string? sort = null)
        => Ok(await comments.GetThreadAsync(CurrentApp.Id, AppUserId, page_key, page, page_size, sort));

    [HttpGet("counts")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<CommentCountsDto>> Counts([FromQuery] string? page_keys)
        => Ok(await comments.CountsAsync(CurrentApp.Id, page_keys));

    [HttpPost]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<PostedCommentDto>> Create([FromBody] CreateCommentRequest req)
    {
        var (comment, honeypot) = await comments.CreateAsync(CurrentApp, AppUserId, req,
            ClientIps.Hash(HttpContext), Request.Headers.UserAgent.ToString());
        var dto = comment.ToPostedDto();
        // Honeypot: look exactly like a normal moderated submission.
        if (honeypot) dto = dto with { Status = CommentStatus.Pending.ToApi() };
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<ActionResult<PostedCommentDto>> Update([FromRoute] Guid id, [FromBody] UpdateCommentRequest req)
        => Ok((await comments.UpdateAsync(CurrentApp, RequireAppUserId(), id, req)).ToPostedDto());

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientWrites)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await comments.DeleteAsync(CurrentApp, RequireAppUserId(), id);
        return NoContent();
    }
}
