using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Client;

[ApiController]
[Route("client/collections/{collection_slug}/items")]
public class ClientItemsController(
    CollectionService collectionService,
    ItemService itemService,
    AttachmentService attachmentService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;
    private Guid? AppUserId =>
        Guid.TryParse(User.FindFirst(AppClaimTypes.AppUserId)?.Value, out var id) ? id : null;

    [HttpGet]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    public async Task<ActionResult<PagedResult<ItemDto>>> List(
        [FromRoute] string collection_slug,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var (items, total) = await itemService.GetForCollectionAsync(collection.Id, page, page_size);
        return Ok(new PagedResult<ItemDto>(
            items.Select(i => i.ToDto()).ToList(), total, page, page_size));
    }

    [HttpGet("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    public async Task<ActionResult<ItemDto>> Get(
        [FromRoute] string collection_slug,
        [FromRoute] Guid id)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, id);
        return Ok(item.ToDto());
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = $"{AuthSchemes.ApiKey},{AuthSchemes.AppUser}")]
    public async Task<ActionResult<ItemDto>> Create(
        [FromRoute] string collection_slug,
        [FromBody] CreateItemRequest req)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.CreateAsync(
            collection.Id, AppUserId, req.Data, collection.Schema, CurrentApp.Id);
        return CreatedAtAction(nameof(Get),
            new { collection_slug, id = item.Id },
            item.ToDto());
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = $"{AuthSchemes.ApiKey},{AuthSchemes.AppUser}")]
    public async Task<ActionResult<ItemDto>> Update(
        [FromRoute] string collection_slug,
        [FromRoute] Guid id,
        [FromBody] UpdateItemRequest req)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, id);
        var updated = await itemService.UpdateAsync(item, req.Data, collection.Schema, CurrentApp.Id);
        return Ok(updated.ToDto());
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = $"{AuthSchemes.ApiKey},{AuthSchemes.AppUser}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string collection_slug,
        [FromRoute] Guid id)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, id);
        await itemService.DeleteAsync(item, attachmentService, CurrentApp.Id);
        return NoContent();
    }
}
