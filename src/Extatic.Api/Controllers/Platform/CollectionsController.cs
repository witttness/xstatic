using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps/{app_slug}/collections")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class CollectionsController(CollectionService collectionService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<List<CollectionDto>>> List()
    {
        var collections = await collectionService.GetForAppAsync(CurrentApp.Id);
        return Ok(collections.Select(c => c.ToDto()).ToList());
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionDto>> Create([FromBody] CreateCollectionRequest req)
    {
        var collection = await collectionService.CreateAsync(
            CurrentApp.Id, req.Name, req.Slug, req.Schema,
            req.AttachmentsEnabled, req.AllowedAttachmentTypes ?? []);
        return CreatedAtAction(nameof(Get),
            new { app_slug = CurrentApp.Slug, slug = collection.Slug },
            collection.ToDto());
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CollectionDto>> Get([FromRoute] string slug)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, slug);
        return Ok(collection.ToDto());
    }

    [HttpPut("{slug}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<CollectionDto>> Update(
        [FromRoute] string slug,
        [FromBody] UpdateCollectionRequest req)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, slug);
        var updated = await collectionService.UpdateAsync(
            collection, req.Name, req.Schema, req.AttachmentsEnabled, req.AllowedAttachmentTypes);
        return Ok(updated.ToDto());
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
