using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Comments;

/// <summary>Dashboard moderation, settings and pages for an App's comments.</summary>
[Route("api/apps/{app_slug}/comments")]
public class PlatformCommentsController(CommentModerationService moderation) : PlatformControllerBase
{
    [HttpGet]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<PagedResult<CommentAdminDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? page_key,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
        => Ok(await moderation.ListAsync(CurrentApp.Id, status, page_key, search, page, page_size));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<CommentAdminDto>> Get([FromRoute] Guid id)
        => Ok((await moderation.GetAsync(CurrentApp.Id, id)).ToAdminDto());

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<CommentAdminDto>> Approve([FromRoute] Guid id) => Moderate(id, CommentStatus.Approved);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<CommentAdminDto>> Reject([FromRoute] Guid id) => Moderate(id, CommentStatus.Rejected);

    [HttpPost("{id:guid}/spam")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<CommentAdminDto>> Spam([FromRoute] Guid id) => Moderate(id, CommentStatus.Spam);

    [HttpPost("bulk")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<BulkModerateResult>> Bulk([FromBody] BulkModerateRequest req)
    {
        var target = CommentModerationService.ParseAction(req.Action);
        return Ok(await moderation.ModerateAsync(CurrentApp, UserId, req.Ids ?? [], target));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await moderation.DeleteAsync(CurrentApp.Id, id);
        return NoContent();
    }

    [HttpGet("stats")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<CommentStatsDto>> Stats()
        => Ok(await moderation.StatsAsync(CurrentApp.Id));

    [HttpGet("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CommentSettingsDto>> GetSettings()
        => Ok((await moderation.GetSettingsAsync(CurrentApp.Id)).ToDto());

    [HttpPut("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CommentSettingsDto>> UpdateSettings([FromBody] UpdateCommentSettingsRequest req)
        => Ok((await moderation.UpdateSettingsAsync(CurrentApp.Id, req)).ToDto());

    [HttpGet("pages")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<PagedResult<CommentPageDto>>> Pages(
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(await moderation.ListPagesAsync(CurrentApp.Id, page, page_size));

    [HttpPost("pages/close")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<IActionResult> ClosePage([FromBody] PageKeyRequest req)
    {
        var page = await moderation.SetPageClosedAsync(CurrentApp.Id, req.PageKey, closed: true);
        return Ok(new { page_key = page.PageKey, is_closed = page.IsClosed });
    }

    [HttpPost("pages/reopen")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<IActionResult> ReopenPage([FromBody] PageKeyRequest req)
    {
        var page = await moderation.SetPageClosedAsync(CurrentApp.Id, req.PageKey, closed: false);
        return Ok(new { page_key = page.PageKey, is_closed = page.IsClosed });
    }

    private async Task<ActionResult<CommentAdminDto>> Moderate(Guid id, CommentStatus target)
        => Ok((await moderation.ModerateOneAsync(CurrentApp, UserId, id, target)).ToAdminDto());
}
