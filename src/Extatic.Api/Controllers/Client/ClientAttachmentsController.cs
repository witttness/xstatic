using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Client;

[ApiController]
[Route("client/collections/{collection_slug}/items/{item_id}/attachments")]
public class ClientAttachmentsController(
    CollectionService collectionService,
    ItemService itemService,
    AttachmentService attachmentService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;
    private Guid? AppUserId =>
        Guid.TryParse(User.FindFirst(AppClaimTypes.AppUserId)?.Value, out var id) ? id : null;

    [HttpGet]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    public async Task<ActionResult<List<AttachmentDto>>> List(
        [FromRoute] string collection_slug,
        [FromRoute] Guid item_id)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, item_id);
        var attachments = await attachmentService.GetForItemAsync(item.Id);
        return Ok(attachments.Select(a => a.ToDto()).ToList());
    }

    [HttpGet("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
    public async Task<ActionResult<AttachmentDto>> Get(
        [FromRoute] string collection_slug,
        [FromRoute] Guid item_id,
        [FromRoute] Guid id)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, item_id);
        var attachment = await attachmentService.GetByIdAsync(item.Id, id);
        return Ok(attachment.ToDto());
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = $"{AuthSchemes.ApiKey},{AuthSchemes.AppUser}")]
    public async Task<ActionResult<AttachmentDto>> Upload(
        [FromRoute] string collection_slug,
        [FromRoute] Guid item_id,
        IFormFile file)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, item_id);

        await using var stream = file.OpenReadStream();
        var attachment = await attachmentService.UploadAsync(
            item, collection, CurrentApp, AppUserId,
            file.FileName, file.ContentType, stream, file.Length);

        return CreatedAtAction(nameof(Get),
            new { collection_slug, item_id, id = attachment.Id },
            attachment.ToDto());
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = $"{AuthSchemes.ApiKey},{AuthSchemes.AppUser}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string collection_slug,
        [FromRoute] Guid item_id,
        [FromRoute] Guid id)
    {
        var collection = await collectionService.GetBySlugAsync(CurrentApp.Id, collection_slug);
        var item = await itemService.GetByIdAsync(collection.Id, item_id);
        var attachment = await attachmentService.GetByIdAsync(item.Id, id);
        await attachmentService.DeleteAsync(attachment);
        return NoContent();
    }
}
