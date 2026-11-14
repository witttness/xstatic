using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Forms;

/// <summary>Platform API: form definitions. Read: any collaborator; write: owner/admin.</summary>
[Route("api/apps/{app_slug}/forms")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class FormsController(FormService forms) : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FormDto>>> List()
    {
        var list = await forms.ListAsync(CurrentApp.Id);
        return Ok(list.Select(x => x.Form.ToDto(x.Counts)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormDto>> Get([FromRoute] Guid id)
    {
        var form = await forms.GetAsync(CurrentApp.Id, id);
        return Ok(form.ToDto(await forms.GetCountsAsync(CurrentApp.Id, id)));
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<FormDto>> Create([FromBody] SaveFormRequest req)
    {
        var form = await forms.CreateAsync(CurrentApp.Id, req);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = form.Id },
            form.ToDto(FormCounts.Empty));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<FormDto>> Update([FromRoute] Guid id, [FromBody] SaveFormRequest req)
    {
        var form = await forms.UpdateAsync(CurrentApp.Id, id, req);
        return Ok(form.ToDto(await forms.GetCountsAsync(CurrentApp.Id, id)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await forms.DeleteAsync(CurrentApp.Id, id);
        return NoContent();
    }
}

/// <summary>Platform API: submissions of one form. Read: any collaborator; write: owner/editor (or admin).</summary>
[Route("api/apps/{app_slug}/forms/{form_id:guid}/submissions")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class FormSubmissionsController(FormService forms) : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<FormSubmissionDto>>> List(
        [FromRoute] Guid form_id,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var (items, total) = await forms.ListSubmissionsAsync(CurrentApp.Id, form_id, status, from, to, page, page_size);
        return Ok(new PagedResult<FormSubmissionDto>(items.Select(s => s.ToDto()).ToList(), total, page, page_size));
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(
        [FromRoute] Guid form_id,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        var form = await forms.GetAsync(CurrentApp.Id, form_id);
        var bytes = await forms.ExportCsvAsync(CurrentApp.Id, form_id, status, from, to);
        return File(bytes, "text/csv", $"{form.Slug}-submissions.csv");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormSubmissionDto>> Get([FromRoute] Guid form_id, [FromRoute] Guid id)
        => Ok((await forms.GetSubmissionAsync(CurrentApp.Id, form_id, id)).ToDto());

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<FormSubmissionDto>> UpdateStatus(
        [FromRoute] Guid form_id, [FromRoute] Guid id, [FromBody] UpdateSubmissionStatusRequest req)
        => Ok((await forms.UpdateStatusAsync(CurrentApp.Id, form_id, id, req.Status)).ToDto());

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<IActionResult> Delete([FromRoute] Guid form_id, [FromRoute] Guid id)
    {
        await forms.DeleteSubmissionAsync(CurrentApp.Id, form_id, id);
        return NoContent();
    }

    /// <summary>Bulk-deletes every submission of this form marked Spam.</summary>
    [HttpDelete("spam")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<BulkDeleteResult>> DeleteSpam([FromRoute] Guid form_id)
        => Ok(new BulkDeleteResult(await forms.DeleteSpamAsync(CurrentApp.Id, form_id)));
}
