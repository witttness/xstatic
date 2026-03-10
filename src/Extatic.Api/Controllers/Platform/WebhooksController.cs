using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps/{app_slug}/webhooks")]
[Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
public class WebhooksController(WebhookService webhookService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<List<WebhookDto>>> List()
    {
        var webhooks = await webhookService.GetForAppAsync(CurrentApp.Id);
        return Ok(webhooks.Select(w => w.ToDto()).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<WebhookDto>> Create([FromBody] CreateWebhookRequest req)
    {
        var webhook = await webhookService.CreateAsync(CurrentApp.Id, req.Url, req.Events);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = webhook.Id }, webhook.ToDto());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WebhookDto>> Get([FromRoute] Guid id)
    {
        var webhook = await webhookService.GetByIdAsync(CurrentApp.Id, id);
        return Ok(webhook.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<WebhookDto>> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateWebhookRequest req)
    {
        var webhook = await webhookService.GetByIdAsync(CurrentApp.Id, id);
        var updated = await webhookService.UpdateAsync(webhook, req.Url, req.Events, req.IsActive);
        return Ok(updated.ToDto());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var webhook = await webhookService.GetByIdAsync(CurrentApp.Id, id);
        await webhookService.DeleteAsync(webhook);
        return NoContent();
    }

    [HttpGet("{id}/logs")]
    public async Task<ActionResult<List<WebhookDeliveryLogDto>>> GetLogs([FromRoute] Guid id)
    {
        var logs = await webhookService.GetLogsAsync(id);
        return Ok(logs.Select(l => l.ToDto()).ToList());
    }

    [HttpPost("{id}/logs/{logId}/retrigger")]
    public async Task<IActionResult> Retrigger([FromRoute] Guid id, [FromRoute] Guid logId)
    {
        await webhookService.RetriggerAsync(id, logId);
        return Accepted();
    }
}
