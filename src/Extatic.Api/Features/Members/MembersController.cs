using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Extatic.Api.Features.Members;

/// <summary>Platform (dashboard) API for membership management.</summary>
[Route("api/apps/{app_slug}/members")]
public class MembersController(MembershipService service) : PlatformControllerBase
{
    // ---------------- plans

    [HttpGet("plans")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<List<MembershipPlanDto>>> ListPlans()
        => Ok(await service.ListPlansAsync(CurrentApp.Id));

    [HttpGet("plans/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<MembershipPlanDto>> GetPlan(Guid id)
        => Ok(await service.GetPlanDtoAsync(CurrentApp.Id, id));

    [HttpPost("plans")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<MembershipPlanDto>> CreatePlan([FromBody] MembershipPlanRequest req)
    {
        var plan = await service.CreatePlanAsync(CurrentApp.Id, req);
        return CreatedAtAction(nameof(GetPlan), new { app_slug = CurrentApp.Slug, id = plan.Id }, plan.ToDto());
    }

    [HttpPut("plans/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<MembershipPlanDto>> UpdatePlan(Guid id, [FromBody] MembershipPlanRequest req)
    {
        await service.UpdatePlanAsync(CurrentApp.Id, id, req);
        return Ok(await service.GetPlanDtoAsync(CurrentApp.Id, id));
    }

    [HttpDelete("plans/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        await service.DeletePlanAsync(CurrentApp.Id, id);
        return NoContent();
    }

    // ---------------- settings

    [HttpGet("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<MemberSettingsDto>> GetSettings()
        => Ok((await service.GetSettingsAsync(CurrentApp.Id)).ToDto());

    [HttpPut("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<MemberSettingsDto>> UpdateSettings([FromBody] UpdateMemberSettingsRequest req)
        => Ok((await service.UpdateSettingsAsync(CurrentApp.Id, req)).ToDto());

    // ---------------- memberships

    [HttpGet]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<PagedResult<MembershipSummaryDto>>> List(
        [FromQuery] string? status = null, [FromQuery] Guid? plan_id = null, [FromQuery] string? q = null,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(await service.ListAsync(CurrentApp.Id, status, plan_id, q, page, page_size));

    [HttpGet("stats")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<MembersStatsDto>> Stats()
        => Ok(await service.GetStatsAsync(CurrentApp.Id));

    [HttpGet("export.csv")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<IActionResult> Export(
        [FromQuery] string? status = null, [FromQuery] Guid? plan_id = null, [FromQuery] string? q = null)
        => File(await service.ExportCsvAsync(CurrentApp.Id, status, plan_id, q), "text/csv", "members.csv");

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<MembershipDetailDto>> Get(Guid id)
        => Ok(await service.GetDetailAsync(CurrentApp.Id, id));

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<MembershipDetailDto>> Create([FromBody] CreateMembershipRequest req)
    {
        var m = await service.CreateAsync(CurrentApp.Id, UserId, req);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = m.Id },
            await service.GetDetailAsync(CurrentApp.Id, m.Id));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Approve(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Approve, req);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Reject(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Reject, req);

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Suspend(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Suspend, req);

    [HttpPost("{id:guid}/reinstate")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Reinstate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Reinstate, req);

    [HttpPost("{id:guid}/renew")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Renew(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Renew, req);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public Task<ActionResult<MembershipDetailDto>> Cancel(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MembershipTransitionRequest? req)
        => Transition(id, MembershipAction.Cancel, req);

    [HttpPost("{id:guid}/change-plan")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<MembershipDetailDto>> ChangePlan(Guid id, [FromBody] ChangePlanRequest req)
    {
        await service.ChangePlanAsync(CurrentApp.Id, id, req.PlanId, UserId);
        return Ok(await service.GetDetailAsync(CurrentApp.Id, id));
    }

    [HttpPut("{id:guid}/notes")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<MembershipDetailDto>> UpdateNotes(Guid id, [FromBody] MembershipNotesRequest req)
    {
        await service.UpdateNotesAsync(CurrentApp.Id, id, req.Notes, UserId);
        return Ok(await service.GetDetailAsync(CurrentApp.Id, id));
    }

    private async Task<ActionResult<MembershipDetailDto>> Transition(Guid id, MembershipAction action,
        MembershipTransitionRequest? req)
    {
        await service.TransitionAsync(CurrentApp.Id, id, action, UserId, req?.Reason);
        return Ok(await service.GetDetailAsync(CurrentApp.Id, id));
    }
}
