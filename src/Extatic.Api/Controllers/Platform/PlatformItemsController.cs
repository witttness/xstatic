using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps/{app_slug}/collections/{collection_slug}/items")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class PlatformItemsController(CollectionService collections, ItemService items) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ItemDto>>> List(
        [FromRoute] string collection_slug,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        if (page < 1 || page_size < 1 || page_size > 100 || page - 1 > int.MaxValue / page_size)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid paging parameters", Detail = "page must be at least 1 and page_size must be between 1 and 100" });
        var collection = await collections.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var (result, total) = await items.GetForCollectionAsync(collection.Id, page, page_size);
        return Ok(new PagedResult<ItemDto>(result.Select(i => i.ToDto()).ToList(), total, page, page_size));
    }

    [HttpGet("{item_id:guid}")]
    public async Task<ActionResult<ItemDto>> Get([FromRoute] string collection_slug, [FromRoute] Guid item_id)
    {
        var collection = await collections.GetBySlugAsync(CurrentApp.Id, collection_slug);
        return Ok((await items.GetByIdAsync(collection.Id, item_id)).ToDto());
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<ItemDto>> Create([FromRoute] string collection_slug, [FromBody] CreateItemRequest request)
    {
        var collection = await collections.GetBySlugAsync(CurrentApp.Id, collection_slug);
        RequireObject(request.Data);
        var item = await items.CreateAsync(collection.Id, null, request.Data, collection.Schema, CurrentApp.Id);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, collection_slug, item_id = item.Id }, item.ToDto());
    }

    [HttpPut("{item_id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<ItemDto>> Update(
        [FromRoute] string collection_slug, [FromRoute] Guid item_id,
        [FromBody] PlatformUpdateItemRequest request)
    {
        if (request.Version < 1)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid version", Detail = "version must be a positive integer" });
        var collection = await collections.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await items.GetByIdAsync(collection.Id, item_id);
        RequireObject(request.Data);
        var updated = await items.UpdateAsync(item, request.Data, collection.Schema, CurrentApp.Id, request.Version);
        return Ok(updated.ToDto());
    }

    private static void RequireObject(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw new ValidationException("Item data must be a JSON object", [new("", "Expected a JSON object")]);
    }
}
