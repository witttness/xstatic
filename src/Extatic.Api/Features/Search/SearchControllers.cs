using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Data;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Search;

/// <summary>Client API: public search for the static site, plus CI indexing with the App's secret key.</summary>
[Route("api/{app_public_id}/search")]
public class ClientSearchController(
    AppDbContext db,
    SearchQueryService queryService,
    SearchDocumentService documentService) : ClientControllerBase
{
    [HttpGet]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 10,
        [FromQuery] string? section = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? source = null,
        [FromQuery] string? collection = null,
        CancellationToken ct = default)
    {
        await EnsurePublicSearchAsync(ct);
        return Ok(await queryService.SearchAsync(CurrentApp.Id,
            new SearchQuery(q, page, page_size, section, tags, source, collection), logQuery: true, ct));
    }

    [HttpGet("suggest")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<SuggestResponse>> Suggest([FromQuery] string? q, CancellationToken ct = default)
    {
        await EnsurePublicSearchAsync(ct);
        return Ok(await queryService.SuggestAsync(CurrentApp.Id, q, ct));
    }

    [HttpPut("documents")]
    [Authorize(Policy = PolicyNames.AppServerKey)]
    public async Task<ActionResult<PushDocumentsResponse>> Push([FromBody] PushDocumentsRequest request,
        CancellationToken ct = default)
        => Ok(await documentService.UpsertPagesAsync(CurrentApp.Id, request, ct));

    [HttpDelete("documents")]
    [Authorize(Policy = PolicyNames.AppServerKey)]
    public async Task<ActionResult<DeleteDocumentsResponse>> Delete([FromBody] ExternalIdsRequest request,
        CancellationToken ct = default)
        => Ok(await documentService.DeletePagesAsync(CurrentApp.Id, request, ct));

    [HttpPost("sync")]
    [Authorize(Policy = PolicyNames.AppServerKey)]
    public async Task<ActionResult<SyncResponse>> Sync([FromBody] ExternalIdsRequest request,
        CancellationToken ct = default)
        => Ok(await documentService.SyncPagesAsync(CurrentApp.Id, request, ct));

    [HttpGet("documents/stats")]
    [Authorize(Policy = PolicyNames.AppServerKey)]
    public async Task<ActionResult<SearchStatsDto>> Stats(CancellationToken ct = default)
        => Ok(await documentService.GetStatsAsync(CurrentApp.Id, ct));

    private async Task EnsurePublicSearchAsync(CancellationToken ct)
    {
        var settings = await SearchSettingsStore.GetAsync(db, CurrentApp.Id, ct);
        if (!settings.Enabled || !settings.PublicSearchEnabled)
            throw new NotFoundException("Search is not enabled for this app");
    }
}

/// <summary>Platform API for the dashboard.</summary>
[Route("api/apps/{app_slug}/search")]
public class SearchPlatformController(
    SearchAdminService adminService,
    SearchDocumentService documentService,
    SearchQueryService queryService) : PlatformControllerBase
{
    [HttpGet]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<SearchOverviewDto>> Overview(CancellationToken ct = default)
        => Ok(await adminService.GetOverviewAsync(CurrentApp, ct));

    [HttpGet("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<SearchSettingsDto>> GetSettings(CancellationToken ct = default)
        => Ok((await adminService.GetSettingsAsync(CurrentApp.Id, ct)).ToDto());

    [HttpPut("settings")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<SearchSettingsDto>> PutSettings([FromBody] UpdateSearchSettingsRequest request,
        CancellationToken ct = default)
        => Ok((await adminService.UpdateSettingsAsync(CurrentApp.Id, request, ct)).ToDto());

    [HttpGet("collections")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<List<CollectionConfigDto>>> ListConfigs(CancellationToken ct = default)
        => Ok(await adminService.ListConfigsAsync(CurrentApp.Id, ct));

    [HttpGet("collections/{collection_slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionConfigDto>> GetConfig([FromRoute] string collection_slug,
        CancellationToken ct = default)
        => Ok(await adminService.GetConfigAsync(CurrentApp.Id, collection_slug, ct));

    [HttpPut("collections/{collection_slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionConfigSaveResult>> PutConfig([FromRoute] string collection_slug,
        [FromBody] PutCollectionConfigRequest request, CancellationToken ct = default)
        => Ok(await adminService.PutConfigAsync(CurrentApp.Id, collection_slug, request, ct));

    [HttpDelete("collections/{collection_slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> DeleteConfig([FromRoute] string collection_slug, CancellationToken ct = default)
    {
        await adminService.DeleteConfigAsync(CurrentApp.Id, collection_slug, ct);
        return NoContent();
    }

    [HttpPost("reindex")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<ReindexJobDto>> Reindex(CancellationToken ct = default)
    {
        var job = await adminService.EnqueueReindexAsync(CurrentApp.Id, null, ct);
        return AcceptedAtAction(nameof(GetJob), new { app_slug = CurrentApp.Slug, id = job.Id }, job.ToDto());
    }

    [HttpGet("reindex")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<List<ReindexJobDto>>> ListJobs(CancellationToken ct = default)
        => Ok((await adminService.ListJobsAsync(CurrentApp.Id, ct)).Select(j => j.ToDto()).ToList());

    [HttpGet("reindex/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<ReindexJobDto>> GetJob([FromRoute] Guid id, CancellationToken ct = default)
        => Ok((await adminService.GetJobAsync(CurrentApp.Id, id, ct)).ToDto());

    [HttpGet("documents")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<PagedResult<SearchDocumentSummaryDto>>> Documents(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20,
        [FromQuery] string? source = null,
        [FromQuery] string? section = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
        => Ok(await documentService.BrowseAsync(CurrentApp.Id, page, page_size, source, section, q, ct));

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<IActionResult> DeleteDocument([FromRoute] Guid id, CancellationToken ct = default)
    {
        await documentService.DeleteDocumentAsync(CurrentApp.Id, id, ct);
        return NoContent();
    }

    /// <summary>Runs a query exactly as the public endpoint would (regardless of enabled flags), without logging it.</summary>
    [HttpGet("test")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<SearchResponse>> Test(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 10,
        [FromQuery] string? section = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? source = null,
        [FromQuery] string? collection = null,
        CancellationToken ct = default)
        => Ok(await queryService.SearchAsync(CurrentApp.Id,
            new SearchQuery(q, page, page_size, section, tags, source, collection), logQuery: false, ct));

    [HttpGet("analytics")]
    [Authorize(Policy = PolicyNames.AppAnyAccess)]
    public async Task<ActionResult<SearchAnalyticsDto>> Analytics(CancellationToken ct = default)
        => Ok(await adminService.GetAnalyticsAsync(CurrentApp.Id, ct));
}
