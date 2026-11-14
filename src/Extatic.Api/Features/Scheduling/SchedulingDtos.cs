using System.Globalization;
using System.Text.Json;

namespace Extatic.Api.Features.Scheduling;

// ---- Platform: configuration ----

/// <summary>Day 0 = Sunday … 6 = Saturday. Times are "HH:mm" local to the calendar; end "24:00" (or "00:00") = midnight.</summary>
public record AvailabilityRuleDto(int DayOfWeek, string StartTime, string EndTime);

public record BookingCalendarDto(
    Guid Id, string Name, string Slug, string Timezone, string[] NotifyEmails, bool IsActive,
    List<AvailabilityRuleDto> Rules, DateTime CreatedAt, DateTime UpdatedAt);

public record SaveBookingCalendarRequest(
    string? Name, string? Slug, string? Timezone, string[]? NotifyEmails, bool? IsActive);

public record ReplaceAvailabilityRulesRequest(List<AvailabilityRuleDto>? Rules);

public record AvailabilityOverrideDto(
    Guid Id, DateOnly Date, bool IsUnavailable, string? StartTime, string? EndTime, string? Reason);

public record SaveAvailabilityOverrideRequest(
    DateOnly? Date, bool IsUnavailable, string? StartTime, string? EndTime, string? Reason);

public record AppointmentTypeDto(
    Guid Id, Guid CalendarId, string Name, string Slug, string? Description,
    int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes, int SlotIntervalMinutes,
    int MinNoticeMinutes, int MaxAdvanceDays, int? MaxPerDay, bool RequiresApproval,
    JsonElement? IntakeSchema, string? Location, string? MeetingUrl, string? ManageUrlTemplate,
    bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);

public record SaveAppointmentTypeRequest(
    Guid? CalendarId, string? Name, string? Slug, string? Description,
    int? DurationMinutes, int? BufferBeforeMinutes, int? BufferAfterMinutes, int? SlotIntervalMinutes,
    int? MinNoticeMinutes, int? MaxAdvanceDays, int? MaxPerDay, bool? RequiresApproval,
    JsonElement? IntakeSchema, string? Location, string? MeetingUrl, string? ManageUrlTemplate, bool? IsActive);

// ---- Slots ----

public record SlotDto(DateTime StartsAt, DateTime EndsAt, string LocalTime);
public record SlotDayDto(DateOnly Date, List<SlotDto> Slots);
public record SlotsResponse(string Timezone, List<SlotDayDto> Days);

// ---- Platform: appointments ----

public record AppointmentDto(
    Guid Id, Guid CalendarId, string CalendarName, string CalendarTimezone,
    Guid AppointmentTypeId, string AppointmentTypeName, string AppointmentTypeSlug, Guid? AppUserId,
    string CustomerName, string CustomerEmail, string? CustomerPhone, string? CustomerTimezone,
    DateTime StartsAt, DateTime EndsAt, string Status, JsonElement Data, string? Notes,
    DateTime? CancelledAt, string? CancelReason, DateTime? ReminderSentAt, DateTime CreatedAt, DateTime UpdatedAt);

public record ManualBookingRequest(
    Guid? AppointmentTypeId, DateTime? StartsAt, string? Name, string? Email, string? Phone,
    string? Timezone, JsonElement? Data, string? Notes, bool? Confirmed);

public record AppointmentReasonRequest(string? Reason);
public record RescheduleAppointmentRequest(DateTime? StartsAt);
public record UpdateAppointmentNotesRequest(string? Notes);

// ---- Client ----

public record PublicAppointmentTypeDto(
    string Slug, string Name, string? Description, int DurationMinutes, bool RequiresApproval,
    string? Location, JsonElement? IntakeSchema, string CalendarName, string Timezone);

public record BookAppointmentRequest(
    DateTime? StartsAt, string? Name, string? Email, string? Phone, string? Timezone, JsonElement? Data);

public record PublicAppointmentDto(
    Guid Id, string Status, string AppointmentTypeSlug, string AppointmentTypeName, string CalendarName,
    string Timezone, string? CustomerTimezone, DateTime StartsAt, DateTime EndsAt,
    string CustomerName, string CustomerEmail, string? CustomerPhone, JsonElement Data,
    string? Location, string? MeetingUrl, DateTime? CancelledAt, string? CancelReason,
    bool CanCancel, bool CanReschedule);

/// <summary>The manage token is returned exactly once; only its hash is stored.</summary>
public record BookAppointmentResponse(PublicAppointmentDto Appointment, string ManageToken);

public static class SchedulingMappings
{
    public static string FormatTime(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string FormatEnd(TimeOnly t) => t == TimeOnly.MinValue ? "24:00" : FormatTime(t);

    public static BookingCalendarDto ToDto(this BookingCalendar c) => new(
        c.Id, c.Name, c.Slug, c.Timezone, c.NotifyEmails, c.IsActive,
        c.Rules.OrderBy(r => r.DayOfWeek).ThenBy(r => r.StartTime)
            .Select(r => new AvailabilityRuleDto((int)r.DayOfWeek, FormatTime(r.StartTime), FormatEnd(r.EndTime)))
            .ToList(),
        c.CreatedAt, c.UpdatedAt);

    public static AvailabilityOverrideDto ToDto(this AvailabilityOverride o) => new(
        o.Id, o.Date, o.IsUnavailable,
        o.StartTime is { } s ? FormatTime(s) : null, o.EndTime is { } e ? FormatEnd(e) : null, o.Reason);

    public static AppointmentTypeDto ToDto(this AppointmentType t) => new(
        t.Id, t.CalendarId, t.Name, t.Slug, t.Description, t.DurationMinutes, t.BufferBeforeMinutes,
        t.BufferAfterMinutes, t.SlotIntervalMinutes, t.MinNoticeMinutes, t.MaxAdvanceDays, t.MaxPerDay,
        t.RequiresApproval, ParseJson(t.IntakeSchema), t.Location, t.MeetingUrl, t.ManageUrlTemplate,
        t.IsActive, t.CreatedAt, t.UpdatedAt);

    public static PublicAppointmentTypeDto ToPublicDto(this AppointmentType t) => new(
        t.Slug, t.Name, t.Description, t.DurationMinutes, t.RequiresApproval, t.Location,
        ParseJson(t.IntakeSchema), t.Calendar.Name, t.Calendar.Timezone);

    /// <summary>Requires Calendar and AppointmentType to be loaded.</summary>
    public static AppointmentDto ToDto(this Appointment a) => new(
        a.Id, a.CalendarId, a.Calendar.Name, a.Calendar.Timezone, a.AppointmentTypeId,
        a.AppointmentType.Name, a.AppointmentType.Slug, a.AppUserId, a.CustomerName, a.CustomerEmail,
        a.CustomerPhone, a.CustomerTimezone, a.StartsAt, a.EndsAt, a.Status.ToString(),
        ParseJson(a.Data) ?? EmptyObject(), a.Notes, a.CancelledAt, a.CancelReason, a.ReminderSentAt,
        a.CreatedAt, a.UpdatedAt);

    /// <summary>Customer-facing view (never includes Notes). Requires Calendar and AppointmentType.</summary>
    public static PublicAppointmentDto ToPublicDto(this Appointment a, DateTime nowUtc)
    {
        var open = a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;
        var beforeNotice = a.StartsAt >= nowUtc.AddMinutes(a.AppointmentType.MinNoticeMinutes);
        return new(
            a.Id, a.Status.ToString(), a.AppointmentType.Slug, a.AppointmentType.Name, a.Calendar.Name,
            a.Calendar.Timezone, a.CustomerTimezone, a.StartsAt, a.EndsAt, a.CustomerName, a.CustomerEmail,
            a.CustomerPhone, ParseJson(a.Data) ?? EmptyObject(), a.AppointmentType.Location,
            a.Status == AppointmentStatus.Confirmed ? a.AppointmentType.MeetingUrl : null,
            a.CancelledAt, a.CancelReason, open && beforeNotice, open && beforeNotice);
    }

    public static JsonElement? ParseJson(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();
}
