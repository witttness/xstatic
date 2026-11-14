using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Forms;

/// <summary>Public form endpoints for static sites. AppUser token optional.</summary>
[Route("api/{app_public_id}/forms")]
[Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
public class ClientFormsController(FormSubmissionService submissions) : ClientControllerBase
{
    [HttpGet("{slug}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PublicFormDto>> Get([FromRoute] string slug)
    {
        var form = await submissions.GetActiveAsync(CurrentApp.Id, slug);
        return Ok(form.ToPublicDto());
    }

    /// <summary>
    /// Accepts application/json, application/x-www-form-urlencoded and multipart/form-data (no files).
    /// The body is read here (not model-bound) so the 64 KB cap applies to every content type.
    /// </summary>
    [HttpPost("{slug}/submissions")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<IActionResult> Submit([FromRoute] string slug, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ct);
        var context = new SubmissionContext(
            AppUserId,
            ClientIps.Hash(HttpContext),
            Request.Headers.UserAgent.ToString(),
            Request.Headers.Referer.ToString());

        var result = await submissions.SubmitAsync(CurrentApp.Id, slug, Request.ContentType, body, context);

        if (result.IsHtmlForm && result.RedirectUrl is not null && !AcceptsJson())
        {
            // Only ever the form's own validated SuccessRedirectUrl — never a client-supplied URL.
            Response.Headers.Location = result.RedirectUrl;
            return StatusCode(StatusCodes.Status303SeeOther);
        }
        return StatusCode(StatusCodes.Status201Created, new SubmissionAcceptedDto(result.Id, result.CreatedAt));
    }

    private bool AcceptsJson()
        => Request.Headers.Accept.Any(a => a is not null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));

    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        const int max = FormPayloadParser.MaxPayloadBytes;
        if (Request.ContentLength > max)
            throw new FileTooLargeException($"Submission exceeds {max / 1024} KB");

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max)
                throw new FileTooLargeException($"Submission exceeds {max / 1024} KB");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
