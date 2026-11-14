using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Features.Events;

[Route("api/{app_public_id}/events")]
[Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
[EventProblemFilter]
public class ClientEventsController(EventService events) : ClientControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PagedResult<ClientEventDto>>> List(
        [FromQuery] bool include_past = false, [FromQuery] int page = 1, [FromQuery] int page_size = 20)
    {
        var (items, total) = await events.ListPublishedAsync(CurrentApp.Id, include_past, page, page_size);
        var availability = await events.AvailabilityAsync(CurrentApp.Id, items.Select(e => e.Id).ToList());
        var now = events.Clock();
        return Ok(new PagedResult<ClientEventDto>(
            items.Select(e => e.ToClientDto(availability.GetValueOrDefault(e.Id, EventAvailability.Empty), now)).ToList(),
            total, page, page_size));
    }

    [HttpGet("{slug}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<ClientEventDto>> Get([FromRoute] string slug)
    {
        var ev = await events.GetPublicAsync(CurrentApp.Id, slug);
        return Ok(ev.ToClientDto(await events.AvailabilityAsync(ev), events.Clock()));
    }

    [HttpGet("{slug}/calendar.ics")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<IActionResult> Calendar([FromRoute] string slug)
    {
        var ev = await events.GetPublicAsync(CurrentApp.Id, slug);
        var ics = IcsBuilder.Build(ev, CurrentApp.Name, events.Clock());
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"{ev.Slug}.ics");
    }

    [HttpPost("{slug}/registrations")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<RegistrationCreatedDto>> Register(
        [FromRoute] string slug, [FromBody] RegisterRequest req)
    {
        var (registration, ev, token) = await events.RegisterAsync(CurrentApp, slug, AppUserId, req);
        var dto = registration.ToClientDto(ev, await events.AvailabilityAsync(ev), events.Clock(),
            await events.WaitlistPositionAsync(registration));
        return StatusCode(StatusCodes.Status201Created, new RegistrationCreatedDto(dto, token));
    }

    [HttpGet("registrations/{token}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<ClientRegistrationDto>> GetByToken([FromRoute] string token)
    {
        var (registration, ev) = await events.GetByTokenAsync(CurrentApp.Id, token);
        return Ok(registration.ToClientDto(ev, await events.AvailabilityAsync(ev), events.Clock(),
            await events.WaitlistPositionAsync(registration)));
    }

    [HttpDelete("registrations/{token}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<IActionResult> CancelByToken([FromRoute] string token)
    {
        await events.CancelByTokenAsync(CurrentApp.Id, token);
        return NoContent();
    }

    [HttpGet("me/registrations")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PagedResult<ClientRegistrationDto>>> Mine(
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
    {
        var (items, total) = await events.ListForAppUserAsync(CurrentApp.Id, RequireAppUserId(), page, page_size);
        var availability = await events.AvailabilityAsync(CurrentApp.Id, items.Select(r => r.EventId).Distinct().ToList());
        var now = events.Clock();
        var dtos = new List<ClientRegistrationDto>();
        foreach (var r in items)
            dtos.Add(r.ToClientDto(r.Event, availability.GetValueOrDefault(r.EventId, EventAvailability.Empty), now,
                await events.WaitlistPositionAsync(r)));
        return Ok(new PagedResult<ClientRegistrationDto>(dtos, total, page, page_size));
    }
}
