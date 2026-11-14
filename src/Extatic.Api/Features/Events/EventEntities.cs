using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Events;

public enum EventStatus
{
    Draft,
    Published,
    Cancelled
}

public enum EventRegistrationStatus
{
    Confirmed,
    Waitlisted,
    Cancelled
}

/// <summary>An event a static site's visitors can register for (table <c>events</c>).</summary>
public class Event : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? OnlineUrl { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    /// <summary>IANA time zone id, e.g. "Europe/London". Times are stored in UTC.</summary>
    public string Timezone { get; set; } = "UTC";
    /// <summary>Confirmed seats allowed; null = unlimited.</summary>
    public int? Capacity { get; set; }
    public bool WaitlistEnabled { get; set; }
    /// <summary>Waitlisted seats allowed; null = unlimited (when the waitlist is enabled).</summary>
    public int? WaitlistCapacity { get; set; }
    public int MaxSeatsPerRegistration { get; set; } = 1;
    public DateTime? RegistrationOpensAt { get; set; }
    public DateTime? RegistrationClosesAt { get; set; }
    /// <summary>JSON Schema (jsonb) for extra attendee data.</summary>
    public string? RegistrationSchema { get; set; }
    public bool RequireAppUser { get; set; }
    /// <summary>https URL on the tenant site containing <c>{token}</c>.</summary>
    public string? ManageUrlTemplate { get; set; }
    public string[] NotifyEmails { get; set; } = [];
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<EventRegistration> Registrations { get; set; } = [];
}

/// <summary>A registration for an <see cref="Event"/> (table <c>event_registrations</c>).</summary>
public class EventRegistration : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid EventId { get; set; }
    public Guid? AppUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Normalised (trimmed, lowercase).</summary>
    public string Email { get; set; } = string.Empty;
    public int Seats { get; set; } = 1;
    public string Data { get; set; } = "{}";
    public EventRegistrationStatus Status { get; set; }
    /// <summary>SHA-256 of the manage token; the raw token is only ever returned to the registrant.</summary>
    public string ManageTokenHash { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? PromotedAt { get; set; }

    public App App { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public AppUser? AppUser { get; set; }
}
