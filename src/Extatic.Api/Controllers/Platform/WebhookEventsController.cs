using Extatic.Api.Auth;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/webhook-events")]
[Authorize(Policy = PolicyNames.PlatformUser)]
public class WebhookEventsController(WebhookEventCatalog catalog) : ControllerBase
{
    /// <summary>All webhook event types a Webhook may subscribe to.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<string>> List() => Ok(catalog.Events);
}
