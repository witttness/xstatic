using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps")]
[Authorize(Policy = PolicyNames.PlatformUser)]
public class AppsController(AppService appService) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(AppClaimTypes.UserId)!.Value);
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<List<AppDto>>> List()
    {
        var apps = await appService.GetForUserAsync(CurrentUserId);
        return Ok(apps.Select(a => a.ToDto()).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CreateAppResponse>> Create([FromBody] CreateAppRequest req)
    {
        var (app, apiKey) = await appService.CreateAsync(CurrentUserId, req.Name, req.Slug);
        return CreatedAtAction(nameof(Get), new { app_slug = app.Slug },
            new CreateAppResponse(app.ToDto(), apiKey));
    }

    [HttpGet("{app_slug}")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public ActionResult<AppDto> Get([FromRoute] string app_slug)
        => Ok(CurrentApp.ToDto());

    [HttpPut("{app_slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<AppDto>> Update(
        [FromRoute] string app_slug,
        [FromBody] UpdateAppRequest req)
    {
        var app = await appService.UpdateAsync(
            CurrentApp, req.Name, req.AllowedOrigins,
            req.MaxFileSizeMb, req.MaxAttachmentsPerItem, req.StorageQuotaGb);
        return Ok(app.ToDto());
    }

    [HttpDelete("{app_slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOnly)]
    public async Task<IActionResult> Delete([FromRoute] string app_slug)
    {
        await appService.DeleteAsync(CurrentApp);
        return NoContent();
    }

    [HttpPost("{app_slug}/api-key/regenerate")]
    [Authorize(Policy = PolicyNames.AppOwnerOnly)]
    public async Task<ActionResult<RegenerateApiKeyResponse>> RegenerateApiKey([FromRoute] string app_slug)
    {
        var newKey = await appService.RegenerateApiKeyAsync(CurrentApp);
        return Ok(new RegenerateApiKeyResponse(newKey));
    }
}
