using System.Text.Json;

namespace Extatic.Api.Features.Events;

/// <summary>Create / full-replace body for an event (platform API). All fields validated by the service (422).</summary>
public class EventRequest
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? OnlineUrl { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Timezone { get; set; }
    public int? Capacity { get; set; }
    public bool WaitlistEnabled { get; set; }
    public int? WaitlistCapacity { get; set; }
    public int? MaxSeatsPerRegistration { get; set; }
    public DateTimeOffset? RegistrationOpensAt { get; set; }
    public DateTimeOffset? RegistrationClosesAt { get; set; }
    public JsonElement? RegistrationSchema { get; set; }
    public bool RequireAppUser { get; set; }
    public string? ManageUrlTemplate { get; set; }
    public string[]? NotifyEmails { get; set; }
}

public record EventStatsDto(
    int ConfirmedRegistrations,
    int ConfirmedSeats,
    int WaitlistedRegistrations,
    int WaitlistedSeats,
    int CancelledRegistrations,
    int CheckedInRegistrations,
    int CheckedInSeats,
    int? Capacity,
    int? SeatsRemaining);

public record EventDto(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    string? Location,
    string? OnlineUrl,
    DateTime StartsAt,
    DateTime EndsAt,
    string Timezone,
    int? Capacity,
    bool WaitlistEnabled,
    int? WaitlistCapacity,
    int MaxSeatsPerRegistration,
    DateTime? RegistrationOpensAt,
    DateTime? RegistrationClosesAt,
    JsonElement? RegistrationSchema,
    bool RequireAppUser,
    string? ManageUrlTemplate,
    string[] NotifyEmails,
    string Status,
    EventStatsDto Stats,
    bool RegistrationOpen,
    string? RegistrationClosedReason,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Public view of an event (client API). Omits OnlineUrl, notify emails and the manage template.</summary>
public record ClientEventDto(
    string Slug,
    string Title,
    string? Description,
    string? Location,
    bool IsOnline,
    DateTime StartsAt,
    DateTime EndsAt,
    string Timezone,
    int? Capacity,
    int? SeatsRemaining,
    int MaxSeatsPerRegistration,
    bool WaitlistEnabled,
    bool WaitlistOpen,
    bool RegistrationOpen,
    string? RegistrationClosedReason,
    DateTime? RegistrationOpensAt,
    DateTime? RegistrationClosesAt,
    JsonElement? RegistrationSchema,
    bool RequireAppUser,
    string Status);

public record RegisterRequest(string? Name, string? Email, int? Seats, JsonElement? Data);

public record ManualRegistrationRequest(
    string? Name, string? Email, int? Seats, JsonElement? Data, bool OverrideCapacity = false);

public record PromoteRequest(bool OverrideCapacity = false);

public record CheckInRequest(bool CheckedIn);

/// <summary>Platform view of a registration.</summary>
public record EventRegistrationDto(
    Guid Id,
    Guid EventId,
    Guid? AppUserId,
    string Name,
    string Email,
    int Seats,
    JsonElement Data,
    string Status,
    DateTime? CheckedInAt,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? CancelledAt,
    DateTime? PromotedAt);

/// <summary>Registrant's own view (token lookup / me). OnlineUrl only for confirmed registrations.</summary>
public record ClientRegistrationDto(
    Guid Id,
    ClientEventDto Event,
    string Name,
    string Email,
    int Seats,
    JsonElement Data,
    string Status,
    int? WaitlistPosition,
    string? OnlineUrl,
    bool CheckedIn,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? CancelledAt,
    DateTime? PromotedAt);

/// <summary>201 response for a new registration. <see cref="ManageToken"/> is returned exactly once.</summary>
public record RegistrationCreatedDto(ClientRegistrationDto Registration, string ManageToken);

/// <summary>201 response for an admin-added registration.</summary>
public record ManualRegistrationCreatedDto(EventRegistrationDto Registration, string ManageToken);
