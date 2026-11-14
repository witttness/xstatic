using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Json.Schema;

namespace Extatic.Api.Features.Events;

/// <summary>Seat counts for one event (non-cancelled registrations).</summary>
public record EventAvailability(
    int ConfirmedRegistrations, int ConfirmedSeats,
    int WaitlistedRegistrations, int WaitlistedSeats,
    int CancelledRegistrations = 0, int CheckedInRegistrations = 0, int CheckedInSeats = 0)
{
    public static readonly EventAvailability Empty = new(0, 0, 0, 0);
}

/// <summary>Reason codes returned when registration is closed.</summary>
public static class RegistrationClosedReasons
{
    public const string NotPublished = "not_published";
    public const string Cancelled = "cancelled";
    public const string Ended = "ended";
    public const string NotYetOpen = "not_yet_open";
    public const string Closed = "closed";
    public const string SoldOut = "sold_out";
}

/// <summary>409 with a machine-readable <see cref="Reason"/> (rendered by <see cref="EventProblemFilter"/>).</summary>
public class RegistrationClosedException(string reason, string message) : ConflictException(message)
{
    public string Reason { get; } = reason;
}

public static class EventRules
{
    public const int MaxDataLength = 16 * 1024;
    public const int MaxNotifyEmails = 10;
    public const int MaxSeatsLimit = 100;
    /// <summary>Slugs that would collide with literal client routes.</summary>
    private static readonly HashSet<string> ReservedSlugs = ["me", "registrations"];

    public static string StatusName(EventStatus s) => s.ToString().ToLowerInvariant();
    public static string StatusName(EventRegistrationStatus s) => s.ToString().ToLowerInvariant();

    public static int? SeatsRemaining(Event ev, EventAvailability a)
        => ev.Capacity is null ? null : Math.Max(0, ev.Capacity.Value - a.ConfirmedSeats);

    /// <summary>Reason the registration window is closed (ignoring seats), or null when open.</summary>
    public static string? WindowClosedReason(Event ev, DateTime now)
    {
        if (ev.Status == EventStatus.Cancelled) return RegistrationClosedReasons.Cancelled;
        if (ev.Status != EventStatus.Published) return RegistrationClosedReasons.NotPublished;
        if (ev.EndsAt <= now) return RegistrationClosedReasons.Ended;
        if (ev.RegistrationOpensAt is { } opens && now < opens) return RegistrationClosedReasons.NotYetOpen;
        if (ev.RegistrationClosesAt is { } closes && now >= closes) return RegistrationClosedReasons.Closed;
        return null;
    }

    public static bool WaitlistHasRoom(Event ev, EventAvailability a, int seats = 1)
        => ev.WaitlistEnabled
           && (ev.WaitlistCapacity is null || a.WaitlistedSeats + seats <= ev.WaitlistCapacity.Value);

    /// <summary>
    /// New registrations are confirmed only when the seats fit AND nobody is waiting (strict FIFO:
    /// newcomers never jump the waitlist).
    /// </summary>
    public static bool CanConfirm(Event ev, EventAvailability a, int seats)
        => a.WaitlistedRegistrations == 0
           && (ev.Capacity is null || a.ConfirmedSeats + seats <= ev.Capacity.Value);

    /// <summary>Overall state: (open, reason). Open when the window is open and a seat or waitlist spot exists.</summary>
    public static (bool Open, string? Reason) RegistrationState(Event ev, EventAvailability a, DateTime now)
    {
        var reason = WindowClosedReason(ev, now);
        if (reason is not null) return (false, reason);
        if (CanConfirm(ev, a, 1) || WaitlistHasRoom(ev, a)) return (true, null);
        return (false, RegistrationClosedReasons.SoldOut);
    }

    public static string ReasonMessage(string reason) => reason switch
    {
        RegistrationClosedReasons.Cancelled => "This event has been cancelled",
        RegistrationClosedReasons.NotPublished => "This event is not open for registration",
        RegistrationClosedReasons.Ended => "This event has ended",
        RegistrationClosedReasons.NotYetOpen => "Registration has not opened yet",
        RegistrationClosedReasons.Closed => "Registration is closed",
        RegistrationClosedReasons.SoldOut => "This event is sold out",
        _ => "Registration is not available"
    };

    public static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    public static bool IsValidTimezone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return false;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return false;
        return tz.HasIanaId || TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _);
    }

    public static TimeZoneInfo FindTimezone(string id)
        => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;

    /// <summary>Validates and applies <paramref name="req"/> to <paramref name="ev"/>. Throws 422 with all errors.</summary>
    public static void Apply(Event ev, EventRequest req, App app)
    {
        var errors = new List<ValidationError>();
        var title = req.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 200)
            errors.Add(new("title", "Title is required (max 200 characters)"));
        if (!Slugs.IsValid(req.Slug))
            errors.Add(new("slug", "Must be 1-100 characters: lowercase letters, digits and hyphens"));
        else if (ReservedSlugs.Contains(req.Slug!))
            errors.Add(new("slug", $"'{req.Slug}' is reserved"));
        if (req.Description is { Length: > 10000 })
            errors.Add(new("description", "Max 10000 characters"));
        if (req.Location is { Length: > 500 })
            errors.Add(new("location", "Max 500 characters"));
        var onlineUrl = string.IsNullOrWhiteSpace(req.OnlineUrl) ? null : req.OnlineUrl.Trim();
        if (onlineUrl is not null && (onlineUrl.Length > 2048
            || !Uri.TryCreate(onlineUrl, UriKind.Absolute, out var online)
            || (online.Scheme != Uri.UriSchemeHttps && online.Scheme != Uri.UriSchemeHttp)))
            errors.Add(new("online_url", "Must be an absolute http(s) URL"));
        if (req.StartsAt is null) errors.Add(new("starts_at", "Required"));
        if (req.EndsAt is null) errors.Add(new("ends_at", "Required"));
        if (req.StartsAt is { } s && req.EndsAt is { } e && e <= s)
            errors.Add(new("ends_at", "Must be after starts_at"));
        var timezone = string.IsNullOrWhiteSpace(req.Timezone) ? "UTC" : req.Timezone.Trim();
        if (!IsValidTimezone(timezone))
            errors.Add(new("timezone", "Must be a valid IANA time zone id, e.g. Europe/London"));
        if (req.Capacity is < 1 or > 1_000_000)
            errors.Add(new("capacity", "Must be between 1 and 1000000 (or null for unlimited)"));
        if (req.WaitlistCapacity is < 1 or > 1_000_000)
            errors.Add(new("waitlist_capacity", "Must be between 1 and 1000000 (or null for unlimited)"));
        var maxSeats = req.MaxSeatsPerRegistration ?? 1;
        if (maxSeats is < 1 or > MaxSeatsLimit)
            errors.Add(new("max_seats_per_registration", $"Must be between 1 and {MaxSeatsLimit}"));
        if (req.RegistrationOpensAt is { } ro && req.RegistrationClosesAt is { } rc && rc <= ro)
            errors.Add(new("registration_closes_at", "Must be after registration_opens_at"));

        string? schema = null;
        if (req.RegistrationSchema is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } rs)
        {
            if (rs.ValueKind != JsonValueKind.Object)
                errors.Add(new("registration_schema", "Must be a JSON Schema object"));
            else
            {
                schema = rs.GetRawText();
                try { JsonSchema.FromText(schema, new BuildOptions { SchemaRegistry = new SchemaRegistry() }); }
                catch (Exception ex) // JsonSchema.Net throws a variety of types for malformed schemas
                {
                    errors.Add(new("registration_schema", "Invalid JSON Schema: " + ex.Message));
                }
            }
        }

        var template = string.IsNullOrWhiteSpace(req.ManageUrlTemplate) ? null : req.ManageUrlTemplate.Trim();
        if (template is not null)
        {
            var probe = template.Replace("{token}", "TOKEN");
            if (template.Length > 2048 || !template.Contains("{token}")
                || !Uri.TryCreate(probe, UriKind.Absolute, out var manageUri) || manageUri.Scheme != Uri.UriSchemeHttps)
                errors.Add(new("manage_url_template", "Must be an https URL containing {token}"));
            else if (app.AllowedOrigins.Length > 0 && !app.AllowedOrigins.Any(o =>
                         string.Equals(o.TrimEnd('/'), manageUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
                errors.Add(new("manage_url_template", "Must be on one of the App's allowed origins"));
        }

        var notify = (req.NotifyEmails ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();
        if (notify.Length > MaxNotifyEmails)
            errors.Add(new("notify_emails", $"At most {MaxNotifyEmails} addresses"));
        for (var i = 0; i < notify.Length; i++)
            if (!EmailQueue.IsValidAddress(notify[i]))
                errors.Add(new($"notify_emails/{i}", "Must be a valid email address"));

        if (errors.Count > 0) throw new ValidationException("Invalid event", errors);

        ev.Title = title!;
        ev.Slug = req.Slug!;
        ev.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description;
        ev.Location = string.IsNullOrWhiteSpace(req.Location) ? null : req.Location.Trim();
        ev.OnlineUrl = onlineUrl;
        ev.StartsAt = Utc(req.StartsAt!.Value);
        ev.EndsAt = Utc(req.EndsAt!.Value);
        ev.Timezone = timezone;
        ev.Capacity = req.Capacity;
        ev.WaitlistEnabled = req.WaitlistEnabled;
        ev.WaitlistCapacity = req.WaitlistCapacity;
        ev.MaxSeatsPerRegistration = maxSeats;
        ev.RegistrationOpensAt = req.RegistrationOpensAt is { } o ? Utc(o) : null;
        ev.RegistrationClosesAt = req.RegistrationClosesAt is { } c ? Utc(c) : null;
        ev.RegistrationSchema = schema;
        ev.RequireAppUser = req.RequireAppUser;
        ev.ManageUrlTemplate = template;
        ev.NotifyEmails = notify;
    }

    /// <summary>Validates registrant input; returns normalised (name, email, seats, data JSON).</summary>
    public static (string Name, string Email, int Seats, string Data) ValidateRegistrant(
        Event ev, string? name, string? email, int? seats, JsonElement? data,
        Validation.JsonSchemaValidator validator, bool enforceMaxSeats = true)
    {
        var errors = new List<ValidationError>();
        var cleanName = name?.Trim();
        if (string.IsNullOrEmpty(cleanName) || cleanName.Length > 200 || cleanName.Any(char.IsControl))
            errors.Add(new("name", "Name is required (max 200 characters)"));
        var cleanEmail = email?.Trim().ToLowerInvariant();
        if (!EmailQueue.IsValidAddress(cleanEmail))
            errors.Add(new("email", "Must be a valid email address"));
        var seatCount = seats ?? 1;
        var max = enforceMaxSeats ? ev.MaxSeatsPerRegistration : MaxSeatsLimit;
        if (seatCount < 1 || seatCount > max)
            errors.Add(new("seats", $"Must be between 1 and {max}"));

        var dataElement = data is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } d
            ? d
            : JsonSerializer.SerializeToElement(new { });
        var raw = dataElement.GetRawText();
        if (dataElement.ValueKind != JsonValueKind.Object)
            errors.Add(new("data", "Must be a JSON object"));
        else if (raw.Length > MaxDataLength)
            errors.Add(new("data", $"Must be at most {MaxDataLength} bytes"));
        else if (ev.RegistrationSchema is not null)
            errors.AddRange(validator.Validate(ev.RegistrationSchema, dataElement)
                .Select(e => new ValidationError("/data" + e.Pointer, e.Message)));

        if (errors.Count > 0) throw new ValidationException("Invalid registration", errors);
        return (cleanName!, cleanEmail!, seatCount, raw);
    }
}
