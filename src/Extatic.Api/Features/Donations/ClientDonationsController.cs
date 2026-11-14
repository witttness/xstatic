using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Extatic.Api.Features.Donations.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Donations;

[Route("api/{app_public_id}/donations")]
public class ClientDonationsController(
    DonationCampaignService campaigns,
    DonationCheckoutService checkout,
    DonationWebhookService webhooks,
    ILogger<ClientDonationsController> logger) : ClientControllerBase
{
    public const int MaxWebhookBodyBytes = 512 * 1024;

    [HttpGet("campaigns")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<List<ClientCampaignDto>>> ListCampaigns()
        => Ok(await campaigns.ListOpenAsync(CurrentApp.Id, DateTime.UtcNow));

    [HttpGet("campaigns/{slug}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<ClientCampaignDto>> GetCampaign([FromRoute] string slug)
        => Ok(await campaigns.GetOpenDtoAsync(CurrentApp.Id, slug, DateTime.UtcNow));

    [HttpPost("checkout")]
    [Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<CheckoutResponse>> Checkout([FromBody] ClientCheckoutRequest req, CancellationToken ct)
    {
        try
        {
            var result = await checkout.CheckoutAsync(CurrentApp.Id, AppUserId, ClientIps.Hash(HttpContext), req, ct);
            return Ok(result);
        }
        catch (PaymentProviderException ex)
        {
            return BadGateway(ex.Message);
        }
    }

    [HttpGet("{donation_id:guid}/status")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<DonationStatusDto>> Status([FromRoute(Name = "donation_id")] Guid donationId)
        => Ok(await checkout.GetStatusAsync(CurrentApp.Id, donationId));

    /// <summary>
    /// Provider → Extatic webhook. Tenant comes from the URL (ApiKey scheme); authenticity comes from
    /// the provider signature over the RAW body. Deliberately no rate limiting (provider retries must
    /// never be dropped).
    /// </summary>
    [HttpPost("webhooks/{provider}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [DisableRateLimiting]
    [RequestSizeLimit(MaxWebhookBodyBytes)]
    public async Task<IActionResult> Webhook([FromRoute] string provider, CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false))
        {
            var buffer = new char[MaxWebhookBodyBytes + 1];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            if (read > MaxWebhookBodyBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            rawBody = new string(buffer, 0, read);
        }

        try
        {
            var outcome = await webhooks.HandleAsync(CurrentApp.Id, provider, rawBody, Request.Headers);
            return Ok(new { received = true, outcome = outcome.ToString().ToLowerInvariant() });
        }
        catch (WebhookSignatureException ex)
        {
            logger.LogWarning("Rejected {Provider} webhook for app {AppId}: {Reason}", provider, CurrentApp.Id, ex.Message);
            return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "Invalid webhook signature" });
        }
    }

    private ObjectResult BadGateway(string message)
        => StatusCode(StatusCodes.Status502BadGateway,
            new ProblemDetails { Status = 502, Title = "Bad Gateway", Detail = message });
}
