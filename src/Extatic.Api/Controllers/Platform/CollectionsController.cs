using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Features.SchemaLibrary;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps/{app_slug}/collections")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class CollectionsController(CollectionService collectionService, IPlatformAdminService platformAdmins)
    : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    private bool IsPlatformAdmin => platformAdmins.IsAdmin(User.FindFirst(AppClaimTypes.UserEmail)?.Value);

    private async Task<List<CollectionDto>> ToDtosAsync(IReadOnlyCollection<Collection> collections)
    {
        var sources = await collectionService.GetSourceSchemaInfoAsync(CurrentApp.Id, collections, IsPlatformAdmin);
        return collections.Select(c =>
            c.SourceSchemaId is { } id && sources.TryGetValue(id, out var info)
                ? c.ToDto(info.Name, info.LatestVersion)
                : c.ToDto()).ToList();
    }

    private async Task<CollectionDto> ToDtoAsync(Collection collection) => (await ToDtosAsync([collection]))[0];

    [HttpGet]
    public async Task<ActionResult<List<CollectionDto>>> List()
    {
        var collections = await collectionService.GetForAppAsync(CurrentApp.Id);
        return Ok(await ToDtosAsync(collections));
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionDto>> Create([FromBody] CreateCollectionRequest req)
    {
        var collection = await collectionService.CreateAsync(
            CurrentApp.Id, req.Name, req.Slug, req.Schema,
            req.AttachmentsEnabled, req.AllowedAttachmentTypes ?? [],
            req.SourceSchemaId, req.SourceSchemaVersion, IsPlatformAdmin);
        return CreatedAtAction(nameof(Get),
            new { app_slug = CurrentApp.Slug, slug = collection.Slug },
            await ToDtoAsync(collection));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CollectionDto>> Get([FromRoute] string slug)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, slug);
        return Ok(await ToDtoAsync(collection));
    }

    [HttpPut("{slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionDto>> Update(
        [FromRoute] string slug,
        [FromBody] UpdateCollectionRequest req)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, slug);
        var updated = await collectionService.UpdateAsync(
            collection, req.Name, req.Schema, req.AttachmentsEnabled, req.AllowedAttachmentTypes,
            req.SourceSchemaId, req.SourceSchemaVersion, req.DetachSourceSchema ?? false, IsPlatformAdmin);
        return Ok(await ToDtoAsync(updated));
    }

    [HttpDelete("{slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] string slug)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, slug);
        await collectionService.DeleteAsync(collection);
        return NoContent();
    }
}
