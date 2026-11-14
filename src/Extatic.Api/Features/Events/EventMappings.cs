using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Extatic.Api.Features.Events;

public static class EventMappings
{
    private static JsonElement? Json(string? raw)
        => raw is null ? null : JsonDocument.Parse(raw).RootElement.Clone();

    public static EventStatsDto ToStatsDto(this Event ev, EventAvailability a)
        => new(a.ConfirmedRegistrations, a.ConfirmedSeats, a.WaitlistedRegistrations, a.WaitlistedSeats,
            a.CancelledRegistrations, a.CheckedInRegistrations, a.CheckedInSeats, ev.Capacity,
            EventRules.SeatsRemaining(ev, a));

    public static EventDto ToDto(this Event ev, EventAvailability a, DateTime now)
    {
        var (open, reason) = EventRules.RegistrationState(ev, a, now);
        return new EventDto(ev.Id, ev.Title, ev.Slug, ev.Description, ev.Location, ev.OnlineUrl,
            ev.StartsAt, ev.EndsAt, ev.Timezone, ev.Capacity, ev.WaitlistEnabled, ev.WaitlistCapacity,
            ev.MaxSeatsPerRegistration, ev.RegistrationOpensAt, ev.RegistrationClosesAt,
            Json(ev.RegistrationSchema), ev.RequireAppUser, ev.ManageUrlTemplate, ev.NotifyEmails,
            EventRules.StatusName(ev.Status), ev.ToStatsDto(a), open, reason, ev.CreatedAt, ev.UpdatedAt);
    }

    public static ClientEventDto ToClientDto(this Event ev, EventAvailability a, DateTime now)
    {
        var (open, reason) = EventRules.RegistrationState(ev, a, now);
        var waitlistOpen = EventRules.WindowClosedReason(ev, now) is null && EventRules.WaitlistHasRoom(ev, a);
        return new ClientEventDto(ev.Slug, ev.Title, ev.Description, ev.Location, ev.OnlineUrl is not null,
            ev.StartsAt, ev.EndsAt, ev.Timezone, ev.Capacity, EventRules.SeatsRemaining(ev, a),
            ev.MaxSeatsPerRegistration, ev.WaitlistEnabled, waitlistOpen, open, reason,
            ev.RegistrationOpensAt, ev.RegistrationClosesAt, Json(ev.RegistrationSchema), ev.RequireAppUser,
            EventRules.StatusName(ev.Status));
    }

    public static EventRegistrationDto ToDto(this EventRegistration r)
        => new(r.Id, r.EventId, r.AppUserId, r.Name, r.Email, r.Seats, Json(r.Data)!.Value,
            EventRules.StatusName(r.Status), r.CheckedInAt, r.CreatedAt, r.ConfirmedAt, r.CancelledAt, r.PromotedAt);

    public static ClientRegistrationDto ToClientDto(this EventRegistration r, Event ev, EventAvailability a,
        DateTime now, int? waitlistPosition)
        => new(r.Id, ev.ToClientDto(a, now), r.Name, r.Email, r.Seats, Json(r.Data)!.Value,
            EventRules.StatusName(r.Status), waitlistPosition,
            r.Status == EventRegistrationStatus.Confirmed && ev.Status == EventStatus.Published ? ev.OnlineUrl : null,
            r.CheckedInAt is not null, r.CreatedAt, r.ConfirmedAt, r.CancelledAt, r.PromotedAt);
}

/// <summary>Renders <see cref="RegistrationClosedException"/> as 409 ProblemDetails with a <c>reason</c> code.</summary>
public sealed class EventProblemFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not RegistrationClosedException ex) return;
        var problem = new ProblemDetails
        {
            Status = 409,
            Title = "Conflict",
            Detail = ex.Message,
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["reason"] = ex.Reason;
        context.Result = new ObjectResult(problem) { StatusCode = 409 };
        context.ExceptionHandled = true;
    }
}
