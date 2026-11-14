using Extatic.Api.Auth;
using Extatic.Api.Common;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Events;

[Route("api/apps/{app_slug}/events")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
[EventProblemFilter]
public class PlatformEventsController(EventService events) : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<EventDto>>> List(
        [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int page_size = 20)
    {
        var (items, total) = await events.ListAsync(CurrentApp.Id, status, page, page_size);
        var availability = await events.AvailabilityAsync(CurrentApp.Id, items.Select(e => e.Id).ToList());
        var now = events.Clock();
        return Ok(new PagedResult<EventDto>(
            items.Select(e => e.ToDto(availability.GetValueOrDefault(e.Id, EventAvailability.Empty), now)).ToList(),
            total, page, page_size));
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventDto>> Create([FromBody] EventRequest req)
    {
        var ev = await events.CreateAsync(CurrentApp, req);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = ev.Id },
            ev.ToDto(EventAvailability.Empty, events.Clock()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventDto>> Get([FromRoute] Guid id)
        => Ok(await ToDtoAsync(await events.GetAsync(CurrentApp.Id, id)));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventDto>> Update([FromRoute] Guid id, [FromBody] EventRequest req)
        => Ok(await ToDtoAsync(await events.UpdateAsync(CurrentApp, id, req)));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await events.DeleteAsync(CurrentApp.Id, id);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventDto>> Publish([FromRoute] Guid id)
        => Ok(await ToDtoAsync(await events.PublishAsync(CurrentApp.Id, id)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventDto>> Cancel([FromRoute] Guid id)
        => Ok(await ToDtoAsync(await events.CancelEventAsync(CurrentApp.Id, id)));

    [HttpGet("{id:guid}/stats")]
    public async Task<ActionResult<EventStatsDto>> Stats([FromRoute] Guid id)
    {
        var ev = await events.GetAsync(CurrentApp.Id, id);
        return Ok(ev.ToStatsDto(await events.AvailabilityAsync(ev)));
    }

    [HttpGet("{id:guid}/registrations")]
    public async Task<ActionResult<PagedResult<EventRegistrationDto>>> Registrations(
        [FromRoute] Guid id, [FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        var (items, total) = await events.ListRegistrationsAsync(CurrentApp.Id, id, status, search, page, page_size);
        return Ok(new PagedResult<EventRegistrationDto>(items.Select(r => r.ToDto()).ToList(), total, page, page_size));
    }

    [HttpGet("{id:guid}/registrations/export.csv")]
    public async Task<IActionResult> Export([FromRoute] Guid id, [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var ev = await events.GetAsync(CurrentApp.Id, id);
        var rows = await events.ExportRegistrationsAsync(CurrentApp.Id, id, status, search);
        var csv = new CsvBuilder().AddRow("id", "name", "email", "seats", "status", "created_at", "confirmed_at",
            "promoted_at", "cancelled_at", "checked_in_at", "data");
        foreach (var r in rows)
            csv.AddRow(r.Id, r.Name, r.Email, r.Seats, EventRules.StatusName(r.Status), r.CreatedAt, r.ConfirmedAt,
                r.PromotedAt, r.CancelledAt, r.CheckedInAt, r.Data);
        return File(csv.ToBytes(), "text/csv", $"{ev.Slug}-registrations.csv");
    }

    [HttpPost("{id:guid}/registrations")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<ManualRegistrationCreatedDto>> AddRegistration(
        [FromRoute] Guid id, [FromBody] ManualRegistrationRequest req)
    {
        var (registration, token) = await events.AddManualAsync(CurrentApp.Id, id, req);
        return StatusCode(StatusCodes.Status201Created, new ManualRegistrationCreatedDto(registration.ToDto(), token));
    }

    [HttpPost("{id:guid}/registrations/{registrationId:guid}/cancel")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventRegistrationDto>> CancelRegistration([FromRoute] Guid id, [FromRoute] Guid registrationId)
        => Ok((await events.CancelRegistrationAsync(CurrentApp.Id, id, registrationId)).ToDto());

    [HttpPost("{id:guid}/registrations/{registrationId:guid}/promote")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventRegistrationDto>> Promote([FromRoute] Guid id, [FromRoute] Guid registrationId,
        [FromBody] PromoteRequest? req)
        => Ok((await events.PromoteAsync(CurrentApp.Id, id, registrationId, req?.OverrideCapacity ?? false)).ToDto());

    [HttpPut("{id:guid}/registrations/{registrationId:guid}/check-in")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<EventRegistrationDto>> CheckIn([FromRoute] Guid id, [FromRoute] Guid registrationId,
        [FromBody] CheckInRequest req)
        => Ok((await events.SetCheckInAsync(CurrentApp.Id, id, registrationId, req.CheckedIn)).ToDto());

    private async Task<EventDto> ToDtoAsync(Event ev)
        => ev.ToDto(await events.AvailabilityAsync(ev), events.Clock());
}
