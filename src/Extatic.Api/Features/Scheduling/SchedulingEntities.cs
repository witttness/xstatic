using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Scheduling;

/// <summary>A bookable resource or person. All appointment types on a calendar share its availability.</summary>
public class BookingCalendar : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    /// <summary>IANA time zone id; availability rules and overrides are local times in this zone.</summary>
    public string Timezone { get; set; } = "UTC";
    public string[] NotifyEmails { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<AvailabilityRule> Rules { get; set; } = [];
    public ICollection<AvailabilityOverride> Overrides { get; set; } = [];
    public ICollection<AppointmentType> AppointmentTypes { get; set; } = [];
}

public class AppointmentType : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid CalendarId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }
    /// <summary>Start-time granularity.</summary>
    public int SlotIntervalMinutes { get; set; } = 30;
    public int MinNoticeMinutes { get; set; } = 120;
    public int MaxAdvanceDays { get; set; } = 60;
    public int? MaxPerDay { get; set; }
    public bool RequiresApproval { get; set; }
    /// <summary>JSON Schema (jsonb) for the intake answers in <see cref="Appointment.Data"/>.</summary>
    public string? IntakeSchema { get; set; }
    public string? Location { get; set; }
    public string? MeetingUrl { get; set; }
    /// <summary>https URL on the tenant's site containing <c>{token}</c>; used for manage links in emails.</summary>
    public string? ManageUrlTemplate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public BookingCalendar Calendar { get; set; } = null!;
}

/// <summary>Weekly opening hours, as local time-of-day in the calendar's time zone.</summary>
public class AvailabilityRule : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid CalendarId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    /// <summary>00:00 means midnight at the end of the day.</summary>
    public TimeOnly EndTime { get; set; }

    public App App { get; set; } = null!;
    public BookingCalendar Calendar { get; set; } = null!;
}

/// <summary>A single local date that is closed, or has custom hours replacing the weekly rules.</summary>
public class AvailabilityOverride : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsUnavailable { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public BookingCalendar Calendar { get; set; } = null!;
}

public enum AppointmentStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    NoShow
}

public class Appointment : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid CalendarId { get; set; }
    public Guid AppointmentTypeId { get; set; }
    public Guid? AppUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerTimezone { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    /// <summary>StartsAt minus the type's buffer-before: what actually occupies the calendar.</summary>
    public DateTime BlockStartsAt { get; set; }
    /// <summary>EndsAt plus the type's buffer-after.</summary>
    public DateTime BlockEndsAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    /// <summary>Intake answers (jsonb).</summary>
    public string Data { get; set; } = "{}";
    /// <summary>Internal notes; never returned on the Client API.</summary>
    public string? Notes { get; set; }
    public string ManageTokenHash { get; set; } = string.Empty;
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public BookingCalendar Calendar { get; set; } = null!;
    public AppointmentType AppointmentType { get; set; } = null!;
    public AppUser? AppUser { get; set; }
}
