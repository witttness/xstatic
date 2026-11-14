using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Scheduling;

/// <summary>Public booking API for static sites. Manage tokens act as bearer secrets for one appointment.</summary>
[Route("api/{app_public_id}/scheduling")]
[Authorize(Policy = PolicyNames.ClientAnonymousOrAppUser)]
public class ClientSchedulingController(AppointmentService appointments, AppDbContext db) : ClientControllerBase
{
    [HttpGet("types")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<List<PublicAppointmentTypeDto>>> ListTypes()
        => (await appointments.ListPublicTypesAsync(CurrentApp.Id)).Select(t => t.ToPublicDto()).ToList();

    [HttpGet("types/{slug}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PublicAppointmentTypeDto>> GetAppointmentType([FromRoute] string slug)
        => (await appointments.GetPublicTypeAsync(CurrentApp.Id, slug)).ToPublicDto();

    [HttpGet("types/{slug}/slots")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<SlotsResponse>> Slots([FromRoute] string slug,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? timezone)
    {
        var type = await appointments.GetPublicTypeAsync(CurrentApp.Id, slug);
        return await appointments.GetSlotsAsync(type, from, to, timezone);
    }

    [HttpPost("types/{slug}/appointments")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<BookAppointmentResponse>> Book([FromRoute] string slug, [FromBody] BookAppointmentRequest req)
    {
        var type = await appointments.GetPublicTypeAsync(CurrentApp.Id, slug);
        var appUserId = AppUserId;
        string? name = req.Name, email = req.Email;
        if (appUserId is { } id && (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)))
        {
            // Signed-in customers may omit name/email; fall back to their profile.
            var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.AppId == CurrentApp.Id && u.Id == id);
            if (string.IsNullOrWhiteSpace(name)) name = user?.DisplayName;
            if (string.IsNullOrWhiteSpace(email)) email = user?.Email;
        }
        var (appointment, token) = await appointments.BookAsync(type,
            new BookingInput(req.StartsAt, name, email, req.Phone, req.Timezone, req.Data), appUserId);
        return StatusCode(StatusCodes.Status201Created,
            new BookAppointmentResponse(appointment.ToPublicDto(appointments.UtcNow), token));
    }

    [HttpGet("appointments/{token}")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<PublicAppointmentDto>> GetAppointment([FromRoute] string token)
        => (await appointments.GetByTokenAsync(CurrentApp.Id, token)).ToPublicDto(appointments.UtcNow);

    [HttpGet("appointments/{token}/calendar.ics")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<IActionResult> Ics([FromRoute] string token)
    {
        var appointment = await appointments.GetByTokenAsync(CurrentApp.Id, token);
        var bytes = Encoding.UTF8.GetBytes(IcsBuilder.Build(appointment, appointments.UtcNow));
        return File(bytes, "text/calendar; charset=utf-8", "appointment.ics");
    }

    [HttpPost("appointments/{token}/cancel")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<PublicAppointmentDto>> Cancel([FromRoute] string token, [FromBody] AppointmentReasonRequest? req)
    {
        await appointments.CancelByCustomerAsync(CurrentApp.Id, token, req?.Reason);
        return (await appointments.GetByTokenAsync(CurrentApp.Id, token)).ToPublicDto(appointments.UtcNow);
    }

    [HttpPost("appointments/{token}/reschedule")]
    [EnableRateLimiting(RateLimitPolicyNames.ClientAnonymousWrites)]
    public async Task<ActionResult<PublicAppointmentDto>> Reschedule([FromRoute] string token, [FromBody] RescheduleAppointmentRequest req)
    {
        await appointments.RescheduleByCustomerAsync(CurrentApp.Id, token, req.StartsAt);
        return (await appointments.GetByTokenAsync(CurrentApp.Id, token)).ToPublicDto(appointments.UtcNow);
    }

    [HttpGet("me/appointments")]
    [Authorize(Policy = PolicyNames.AuthenticatedAppUser)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientReads)]
    public async Task<ActionResult<List<PublicAppointmentDto>>> MyAppointments()
    {
        var appUserId = RequireAppUserId();
        var now = appointments.UtcNow;
        return (await appointments.ListForAppUserAsync(CurrentApp.Id, appUserId)).Select(a => a.ToPublicDto(now)).ToList();
    }
}
