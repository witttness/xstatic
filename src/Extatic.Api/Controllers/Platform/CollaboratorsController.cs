using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("apps/{app_slug}/collaborators")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class CollaboratorsController(CollaboratorService collaboratorService) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(AppClaimTypes.UserId)!.Value);
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<List<CollaboratorDto>>> List()
    {
        var collaborators = await collaboratorService.GetForAppAsync(CurrentApp.Id);
        return Ok(collaborators.Select(c => c.ToDto()).ToList());
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollaboratorDto>> Invite([FromBody] InviteCollaboratorRequest req)
    {
        var collaborator = await collaboratorService.InviteAsync(
            CurrentApp.Id, req.Email, req.Role, CurrentUserId);
        return CreatedAtAction(nameof(List), new { app_slug = CurrentApp.Slug }, collaborator.ToDto());
    }

    [HttpPut("{collaboratorId}/role")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollaboratorDto>> UpdateRole(
        [FromRoute] Guid collaboratorId,
        [FromBody] UpdateCollaboratorRoleRequest req)
    {
        var collaborator = await collaboratorService.UpdateRoleAsync(
            CurrentApp.Id, collaboratorId, req.Role);
        return Ok(collaborator.ToDto());
    }

    [HttpDelete("{collaboratorId}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Remove([FromRoute] Guid collaboratorId)
    {
        await collaboratorService.RemoveAsync(CurrentApp.Id, collaboratorId);
        return NoContent();
    }

    [HttpPost("accept")]
    public async Task<ActionResult<CollaboratorDto>> Accept()
    {
        var collaborator = await collaboratorService.AcceptInvitationAsync(CurrentApp.Id, CurrentUserId);
        return Ok(collaborator.ToDto());
    }
}
