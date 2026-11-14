using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Features.Scheduling;

/// <summary>Calendars, weekly rules, overrides. Reads: any collaborator; changes: owner/admin.</summary>
[Route("api/apps/{app_slug}/scheduling/calendars")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class SchedulingCalendarsController(SchedulingConfigService config, AppointmentService appointments) : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<BookingCalendarDto>>> List()
        => (await config.ListCalendarsAsync(CurrentApp.Id)).Select(c => c.ToDto()).ToList();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingCalendarDto>> Get([FromRoute] Guid id)
        => (await config.GetCalendarAsync(CurrentApp.Id, id)).ToDto();

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<BookingCalendarDto>> Create([FromBody] SaveBookingCalendarRequest req)
    {
        var calendar = await config.CreateCalendarAsync(CurrentApp.Id, req);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = calendar.Id }, calendar.ToDto());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<BookingCalendarDto>> Update([FromRoute] Guid id, [FromBody] SaveBookingCalendarRequest req)
        => (await config.UpdateCalendarAsync(CurrentApp.Id, id, req)).ToDto();

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await config.DeleteCalendarAsync(CurrentApp.Id, id);
        return NoContent();
    }

    /// <summary>Replace-all weekly hours.</summary>
    [HttpPut("{id:guid}/rules")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<BookingCalendarDto>> ReplaceRules([FromRoute] Guid id, [FromBody] ReplaceAvailabilityRulesRequest req)
        => (await config.ReplaceRulesAsync(CurrentApp.Id, id, req)).ToDto();

    [HttpGet("{id:guid}/overrides")]
    public async Task<ActionResult<List<AvailabilityOverrideDto>>> ListOverrides([FromRoute] Guid id,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => (await config.ListOverridesAsync(CurrentApp.Id, id, from, to)).Select(o => o.ToDto()).ToList();

    [HttpPost("{id:guid}/overrides")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<AvailabilityOverrideDto>> CreateOverride([FromRoute] Guid id, [FromBody] SaveAvailabilityOverrideRequest req)
        => StatusCode(StatusCodes.Status201Created, (await config.CreateOverrideAsync(CurrentApp.Id, id, req)).ToDto());

    [HttpPut("{id:guid}/overrides/{overrideId:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<AvailabilityOverrideDto>> UpdateOverride([FromRoute] Guid id, [FromRoute] Guid overrideId,
        [FromBody] SaveAvailabilityOverrideRequest req)
        => (await config.UpdateOverrideAsync(CurrentApp.Id, id, overrideId, req)).ToDto();

    [HttpDelete("{id:guid}/overrides/{overrideId:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> DeleteOverride([FromRoute] Guid id, [FromRoute] Guid overrideId)
    {
        await config.DeleteOverrideAsync(CurrentApp.Id, id, overrideId);
        return NoContent();
    }

    /// <summary>
    /// Slots for the dashboard. With <c>type_id</c>: that type's slots (min-notice bypassed, as for manual
    /// bookings). Without: raw availability at <c>duration</c> minutes (default 30).
    /// </summary>
    [HttpGet("{id:guid}/preview-slots")]
    public async Task<ActionResult<SlotsResponse>> PreviewSlots([FromRoute] Guid id, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] Guid? type_id, [FromQuery] int duration = 30)
    {
        var calendar = await config.GetCalendarAsync(CurrentApp.Id, id);
        if (type_id is { } typeId)
        {
            var type = await config.GetTypeAsync(CurrentApp.Id, typeId);
            if (type.CalendarId != calendar.Id) return NotFound();
            return await appointments.GetSlotsAsync(type, from, to, null, enforceMinNotice: false);
        }
        return await appointments.PreviewCalendarSlotsAsync(calendar, duration, from, to);
    }
}

[Route("api/apps/{app_slug}/scheduling/types")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class SchedulingTypesController(SchedulingConfigService config) : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AppointmentTypeDto>>> List([FromQuery] Guid? calendar_id)
        => (await config.ListTypesAsync(CurrentApp.Id, calendar_id)).Select(t => t.ToDto()).ToList();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentTypeDto>> Get([FromRoute] Guid id)
        => (await config.GetTypeAsync(CurrentApp.Id, id)).ToDto();

    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<AppointmentTypeDto>> Create([FromBody] SaveAppointmentTypeRequest req)
    {
        var type = await config.CreateTypeAsync(CurrentApp.Id, req);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = type.Id }, type.ToDto());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<ActionResult<AppointmentTypeDto>> Update([FromRoute] Guid id, [FromBody] SaveAppointmentTypeRequest req)
        => (await config.UpdateTypeAsync(CurrentApp.Id, id, req)).ToDto();

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrAdmin)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await config.DeleteTypeAsync(CurrentApp.Id, id);
        return NoContent();
    }
}

/// <summary>Appointments. Reads: any collaborator; actions and export: owner/editor (and admin).</summary>
[Route("api/apps/{app_slug}/scheduling/appointments")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class SchedulingAppointmentsController(AppointmentService appointments, SchedulingConfigService config) : PlatformControllerBase
{
    [HttpGet]
    public Task<PagedResult<AppointmentDto>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? calendar_id, [FromQuery] Guid? type_id, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int page_size = 50)
        => appointments.ListAsync(CurrentApp.Id, new AppointmentFilter(from, to, calendar_id, type_id, status), page, page_size);

    [HttpGet("export.csv")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? calendar_id, [FromQuery] Guid? type_id, [FromQuery] string? status)
    {
        var bytes = await appointments.ExportCsvAsync(CurrentApp.Id, new AppointmentFilter(from, to, calendar_id, type_id, status));
        return File(bytes, "text/csv", "appointments.csv");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> Get([FromRoute] Guid id)
        => (await appointments.GetAsync(CurrentApp.Id, id)).ToDto();

    /// <summary>Manual booking: bypasses min-notice, never the overlap constraint.</summary>
    [HttpPost]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Create([FromBody] ManualBookingRequest req)
    {
        var typeId = req.AppointmentTypeId ?? throw SchedulingValidation.Invalid("appointment_type_id", "Required");
        var type = await config.GetTypeAsync(CurrentApp.Id, typeId);
        var (appointment, _) = await appointments.BookAsync(type,
            new BookingInput(req.StartsAt, req.Name, req.Email, req.Phone, req.Timezone, req.Data),
            appUserId: null, byAdmin: true, confirm: req.Confirmed, notes: req.Notes);
        return CreatedAtAction(nameof(Get), new { app_slug = CurrentApp.Slug, id = appointment.Id },
            (await appointments.GetAsync(CurrentApp.Id, appointment.Id)).ToDto());
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> UpdateNotes([FromRoute] Guid id, [FromBody] UpdateAppointmentNotesRequest req)
        => (await appointments.UpdateNotesAsync(CurrentApp.Id, id, req.Notes)).ToDto();

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Confirm([FromRoute] Guid id)
        => (await appointments.ConfirmAsync(CurrentApp.Id, id)).ToDto();

    [HttpPost("{id:guid}/decline")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Decline([FromRoute] Guid id, [FromBody] AppointmentReasonRequest? req)
        => (await appointments.DeclineAsync(CurrentApp.Id, id, req?.Reason)).ToDto();

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Cancel([FromRoute] Guid id, [FromBody] AppointmentReasonRequest? req)
        => (await appointments.CancelByAdminAsync(CurrentApp.Id, id, req?.Reason)).ToDto();

    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Reschedule([FromRoute] Guid id, [FromBody] RescheduleAppointmentRequest req)
        => (await appointments.RescheduleByAdminAsync(CurrentApp.Id, id, req.StartsAt)).ToDto();

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> Complete([FromRoute] Guid id)
        => (await appointments.CompleteAsync(CurrentApp.Id, id)).ToDto();

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = PolicyNames.AppOwnerOrEditor)]
    public async Task<ActionResult<AppointmentDto>> NoShow([FromRoute] Guid id)
        => (await appointments.MarkNoShowAsync(CurrentApp.Id, id)).ToDto();
}
