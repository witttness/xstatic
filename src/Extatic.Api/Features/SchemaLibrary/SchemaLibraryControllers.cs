using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>Global library, managed by platform admins (emails in <c>Platform:AdminEmails</c>).</summary>
[Route("api/admin/schemas")]
[Authorize(Policy = PolicyNames.PlatformAdmin)]
public class AdminSchemasController(SchemaLibraryService service) : PlatformControllerBase
{
    private static readonly LibraryScope Scope = LibraryScope.Global;

    [HttpGet]
    public async Task<ActionResult<PagedResult<LibrarySchemaSummaryDto>>> List(
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? visibility,
        [FromQuery] bool? archived, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 50)
        => Ok(await service.ListAsync(Scope, new LibrarySchemaListQuery(q, category, visibility, archived, page, pageSize)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LibrarySchemaDto>> Get(Guid id) => Ok(await service.GetAsync(Scope, id));

    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<ActionResult<LibrarySchemaVersionDto>> GetVersion(Guid id, int version)
        => Ok(await service.GetVersionAsync(Scope, id, version));

    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<List<SchemaUsageDto>>> Usage(Guid id) => Ok(await service.UsageAsync(Scope, id));

    [HttpPost]
    public async Task<ActionResult<LibrarySchemaDto>> Create([FromBody] CreateLibrarySchemaRequest req)
    {
        var created = await service.CreateAsync(Scope, req, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LibrarySchemaDto>> Update(Guid id, [FromBody] UpdateLibrarySchemaRequest req)
        => Ok(await service.UpdateAsync(Scope, id, req, UserId));

    [HttpPut("{id:guid}/visibility")]
    public async Task<ActionResult<LibrarySchemaDto>> ChangeVisibility(Guid id, [FromBody] ChangeVisibilityRequest req)
        => Ok(await service.SetVisibilityAsync(id, req.Visibility, UserId));

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<LibrarySchemaDto>> Archive(Guid id)
        => Ok(await service.SetArchivedAsync(Scope, id, true, UserId));

    [HttpPost("{id:guid}/unarchive")]
    public async Task<ActionResult<LibrarySchemaDto>> Unarchive(Guid id)
        => Ok(await service.SetArchivedAsync(Scope, id, false, UserId));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await service.DeleteAsync(Scope, id);
        return NoContent();
    }

    [HttpPost("validate")]
    public ActionResult<ValidateSchemaResponse> Validate([FromBody] ValidateSchemaRequest req) => Ok(service.Validate(req));
}

/// <summary>
/// One App's private library (write: owner/admin; read: owner/admin/editor) plus the picker
/// (<c>available</c>) used when editing a Collection's schema.
/// </summary>
[Route("api/apps/{app_slug}/schemas")]
[Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
public class AppSchemasController(SchemaLibraryService service, IPlatformAdminService platformAdmins)
    : PlatformControllerBase
{
    private LibraryScope Scope => LibraryScope.ForApp(CurrentApp.Id);
    private bool IsPlatformAdmin => platformAdmins.IsAdmin(UserEmail);

    [HttpGet]
    public async Task<ActionResult<PagedResult<LibrarySchemaSummaryDto>>> List(
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] bool? archived,
        [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 50)
        => Ok(await service.ListAsync(Scope, new LibrarySchemaListQuery(q, category, null, archived, page, pageSize)));

    [HttpGet("available")]
    public async Task<ActionResult<PagedResult<AvailableSchemaDto>>> Available(
        [FromQuery] string? q, [FromQuery] string? category,
        [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 100)
        => Ok(await service.ListAvailableAsync(CurrentApp.Id, IsPlatformAdmin, q, category, page, pageSize));

    [HttpGet("available/{id:guid}")]
    public async Task<ActionResult<AvailableSchemaDetailDto>> GetAvailable(Guid id)
        => Ok(await service.GetAvailableAsync(CurrentApp.Id, IsPlatformAdmin, id));

    [HttpGet("available/{id:guid}/versions/{version:int}")]
    public async Task<ActionResult<LibrarySchemaVersionDto>> GetAvailableVersion(Guid id, int version)
        => Ok(await service.GetAvailableVersionAsync(CurrentApp.Id, IsPlatformAdmin, id, version));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LibrarySchemaDto>> Get(Guid id) => Ok(await service.GetAsync(Scope, id));

    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<ActionResult<LibrarySchemaVersionDto>> GetVersion(Guid id, int version)
        => Ok(await service.GetVersionAsync(Scope, id, version));

    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<List<SchemaUsageDto>>> Usage(Guid id) => Ok(await service.UsageAsync(Scope, id));

    [HttpPost("validate")]
    public ActionResult<ValidateSchemaResponse> Validate([FromBody] ValidateSchemaRequest req) => Ok(service.Validate(req));

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<LibrarySchemaDto>> Create([FromBody] CreateLibrarySchemaRequest req)
    {
        var created = await service.CreateAsync(Scope, req, UserId);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<LibrarySchemaDto>> Update(Guid id, [FromBody] UpdateLibrarySchemaRequest req)
        => Ok(await service.UpdateAsync(Scope, id, req, UserId));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<LibrarySchemaDto>> Archive(Guid id)
        => Ok(await service.SetArchivedAsync(Scope, id, true, UserId));

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<LibrarySchemaDto>> Unarchive(Guid id)
        => Ok(await service.SetArchivedAsync(Scope, id, false, UserId));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await service.DeleteAsync(Scope, id);
        return NoContent();
    }
}
