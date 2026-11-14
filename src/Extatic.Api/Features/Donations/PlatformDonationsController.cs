using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Extatic.Api.Features.Donations.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Donations;

[Route("api/apps/{app_slug}/donations")]
public class PlatformDonationsController(
    PaymentProviderConfigService configs,
    DonationCampaignService campaigns,
    DonationAdminService donations,
    IConfiguration configuration) : PlatformControllerBase
{
    // ---- Provider configuration ---------------------------------------------------------------

    [HttpGet("providers/{provider}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<ProviderConfigDto>> GetProvider([FromRoute] string provider)
        => Ok(await configs.GetAsync(CurrentApp.Id, provider, WebhookUrl(provider)));

    [HttpPut("providers/{provider}")]
    [Authorize(Policy = PolicyNames.AppOwnerOnly)]
    public async Task<ActionResult<ProviderConfigDto>> PutProvider([FromRoute] string provider,
        [FromBody] PutProviderConfigRequest req)
        => Ok(await configs.PutAsync(CurrentApp.Id, provider, req, WebhookUrl(provider)));

    [HttpDelete("providers/{provider}")]
    [Authorize(Policy = PolicyNames.AppOwnerOnly)]
    public async Task<IActionResult> DeleteProvider([FromRoute] string provider)
    {
        await configs.DeleteAsync(CurrentApp.Id, provider);
        return NoContent();
    }

    /// <summary>
    /// The URL to paste into the provider's webhook settings. Uses <c>Donations:PublicApiBaseUrl</c>
    /// when set (recommended behind proxies), else the current request's scheme and host.
    /// </summary>
    private string WebhookUrl(string provider)
    {
        var baseUrl = configuration["Donations:PublicApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl.TrimEnd('/')}/api/{CurrentApp.PublicId}/donations/webhooks/{Uri.EscapeDataString(provider)}";
    }

    // ---- Campaigns ----------------------------------------------------------------------------

    [HttpGet("campaigns")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<List<CampaignDto>>> ListCampaigns()
        => Ok(await campaigns.ListAsync(CurrentApp.Id));

    [HttpPost("campaigns")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CampaignDto>> CreateCampaign([FromBody] CampaignRequest req)
    {
        var created = await campaigns.CreateAsync(CurrentApp.Id, req);
        return CreatedAtAction(nameof(GetCampaign), new { app_slug = CurrentApp.Slug, id = created.Id }, created);
    }

    [HttpGet("campaigns/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CampaignDto>> GetCampaign([FromRoute] Guid id)
        => Ok(await campaigns.GetDtoAsync(CurrentApp.Id, id));

    [HttpPut("campaigns/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CampaignDto>> UpdateCampaign([FromRoute] Guid id, [FromBody] CampaignRequest req)
        => Ok(await campaigns.UpdateAsync(CurrentApp.Id, id, req));

    [HttpDelete("campaigns/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> DeleteCampaign([FromRoute] Guid id)
    {
        await campaigns.DeleteAsync(CurrentApp.Id, id);
        return NoContent();
    }

    // ---- Donations ----------------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<PagedResult<DonationDto>>> List(
        [FromQuery] string? status, [FromQuery(Name = "campaign_id")] Guid? campaignId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20)
        => Ok(await donations.ListAsync(CurrentApp.Id, new DonationQuery(status, campaignId, from, to, q), page, pageSize));

    [HttpGet("export.csv")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<IActionResult> Export(
        [FromQuery] string? status, [FromQuery(Name = "campaign_id")] Guid? campaignId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? q)
    {
        var bytes = await donations.ExportCsvAsync(CurrentApp.Id, new DonationQuery(status, campaignId, from, to, q));
        return File(bytes, "text/csv", "donations.csv");
    }

    [HttpGet("summary")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<DonationSummaryDto>> Summary()
        => Ok(await donations.SummaryAsync(CurrentApp.Id));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<DonationDto>> Get([FromRoute] Guid id)
        => Ok(await donations.GetAsync(CurrentApp.Id, id));

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<DonationDto>> Refund([FromRoute] Guid id, [FromBody] RefundDonationRequest? req,
        CancellationToken ct)
    {
        try
        {
            return Ok(await donations.RefundAsync(CurrentApp.Id, id, req?.AmountMinor, ct));
        }
        catch (PaymentProviderException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ProblemDetails { Status = 502, Title = "Bad Gateway", Detail = ex.Message });
        }
    }
}
