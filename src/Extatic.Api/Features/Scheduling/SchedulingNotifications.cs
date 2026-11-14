using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Notifications;
using Extatic.Api.Webhooks;

namespace Extatic.Api.Features.Scheduling;

public static class SchedulingEvents
{
    public const string Booked = "appointment.booked";
    public const string Confirmed = "appointment.confirmed";
    public const string Cancelled = "appointment.cancelled";
    public const string Rescheduled = "appointment.rescheduled";
    public const string Completed = "appointment.completed";
    public static readonly IReadOnlyList<string> All = [Booked, Confirmed, Cancelled, Rescheduled, Completed];
}

public enum AppointmentEmailKind { Booked, Approved, Declined, Cancelled, Rescheduled, Reminder }

/// <summary>
/// Stages emails (IEmailQueue) and webhook deliveries (WebhookDispatcher.StageAsync) into the caller's
/// DbContext; the caller's SaveChanges/transaction commits them atomically with the appointment change.
/// All user-supplied content is HTML-encoded in HTML bodies.
/// </summary>
public class SchedulingNotifier(IEmailQueue emailQueue, WebhookDispatcher webhooks)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Requires Calendar and AppointmentType to be loaded.</summary>
    public Task StageWebhookAsync(AppDbContext db, Appointment a, string eventType, DateTime? previousStartsAt = null)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            id = a.Id,
            status = a.Status.ToString(),
            calendar_id = a.CalendarId,
            calendar_slug = a.Calendar.Slug,
            appointment_type_id = a.AppointmentTypeId,
            appointment_type_slug = a.AppointmentType.Slug,
            starts_at = a.StartsAt,
            ends_at = a.EndsAt,
            previous_starts_at = previousStartsAt,
            customer_name = a.CustomerName,
            customer_email = a.CustomerEmail,
            customer_phone = a.CustomerPhone,
            customer_timezone = a.CustomerTimezone,
            app_user_id = a.AppUserId,
            data = SchedulingMappings.ParseJson(a.Data),
            cancel_reason = a.CancelReason
        }, JsonOptions);
        return webhooks.StageAsync(db, a.AppId, eventType, data);
    }

    /// <summary>Email to the customer. <paramref name="manageToken"/> is only known at booking time.</summary>
    public void StageCustomerEmail(AppDbContext db, Appointment a, AppointmentEmailKind kind, string? manageToken = null,
        DateTime? previousStartsAt = null)
    {
        var type = a.AppointmentType;
        var zone = DisplayZone(a);
        var (subject, intro) = kind switch
        {
            AppointmentEmailKind.Booked when a.Status == AppointmentStatus.Pending =>
                ($"Booking request received: {type.Name}", "We received your booking request. You will get another email once it has been reviewed."),
            AppointmentEmailKind.Booked => ($"Booking confirmed: {type.Name}", "Your booking is confirmed."),
            AppointmentEmailKind.Approved => ($"Booking confirmed: {type.Name}", "Your booking request has been approved."),
            AppointmentEmailKind.Declined => ($"Booking request declined: {type.Name}", "Unfortunately your booking request could not be accepted."),
            AppointmentEmailKind.Cancelled => ($"Booking cancelled: {type.Name}", "Your booking has been cancelled."),
            AppointmentEmailKind.Rescheduled => ($"Booking rescheduled: {type.Name}", "Your booking has been moved to a new time."),
            _ => ($"Reminder: {type.Name} on {FormatDate(a.StartsAt, zone)}", "This is a reminder of your upcoming appointment.")
        };

        var body = new EmailBody();
        body.Paragraph($"Hello {a.CustomerName},");
        body.Paragraph(intro);
        body.Field("What", type.Name);
        body.Field("When", FormatRange(a.StartsAt, a.EndsAt, zone));
        if (previousStartsAt is { } previous) body.Field("Previously", FormatRange(previous, previous + (a.EndsAt - a.StartsAt), zone));
        if (type.Location is { } location) body.Field("Where", location);
        if (a.Status == AppointmentStatus.Confirmed && type.MeetingUrl is { } meeting
            && kind is not (AppointmentEmailKind.Cancelled or AppointmentEmailKind.Declined))
            body.Link("Join", meeting);
        if (a.CancelReason is { } reason && kind is AppointmentEmailKind.Cancelled or AppointmentEmailKind.Declined)
            body.Field("Reason", reason);
        if (manageToken is not null && type.ManageUrlTemplate is { } template)
            body.Link("View, reschedule or cancel", template.Replace("{token}", Uri.EscapeDataString(manageToken)));

        emailQueue.Stage(db, a.AppId, new EmailMessage(a.CustomerEmail, subject, body.Text, body.Html, null,
            $"scheduling.{kind.ToString().ToLowerInvariant()}"));
    }

    /// <summary>Staff notification to the calendar's NotifyEmails.</summary>
    public void StageStaffEmails(AppDbContext db, Appointment a, AppointmentEmailKind kind, DateTime? previousStartsAt = null)
    {
        var calendar = a.Calendar;
        if (calendar.NotifyEmails.Length == 0) return;
        var zone = CalendarZone(a);
        var verb = kind switch
        {
            AppointmentEmailKind.Booked when a.Status == AppointmentStatus.Pending => "New booking request (needs approval)",
            AppointmentEmailKind.Booked => "New booking",
            AppointmentEmailKind.Cancelled => "Booking cancelled",
            AppointmentEmailKind.Rescheduled => "Booking rescheduled",
            _ => $"Booking {kind.ToString().ToLowerInvariant()}"
        };
        var body = new EmailBody();
        body.Paragraph($"{verb} on {calendar.Name}.");
        body.Field("Type", a.AppointmentType.Name);
        body.Field("When", FormatRange(a.StartsAt, a.EndsAt, zone));
        if (previousStartsAt is { } previous) body.Field("Previously", FormatRange(previous, previous + (a.EndsAt - a.StartsAt), zone));
        body.Field("Customer", a.CustomerName);
        body.Field("Email", a.CustomerEmail);
        if (a.CustomerPhone is { } phone) body.Field("Phone", phone);
        if (a.CancelReason is { } reason && kind == AppointmentEmailKind.Cancelled) body.Field("Reason", reason);
        body.Field("Status", a.Status.ToString());

        foreach (var to in calendar.NotifyEmails)
            emailQueue.Stage(db, a.AppId, new EmailMessage(to, $"{verb}: {a.AppointmentType.Name} – {a.CustomerName}",
                body.Text, body.Html, a.CustomerEmail, "scheduling.staff"));
    }

    public static TimeZoneInfo CalendarZone(Appointment a)
        => SlotEngine.TryFindZone(a.Calendar.Timezone, out var z) ? z : TimeZoneInfo.Utc;

    /// <summary>The customer's zone when supplied and valid, else the calendar's.</summary>
    public static TimeZoneInfo DisplayZone(Appointment a)
        => SlotEngine.TryFindZone(a.CustomerTimezone, out var z) ? z : CalendarZone(a);

    /// <summary>e.g. "Monday, 16 November 2026, 10:00–10:30 (Europe/Berlin)" — always names the zone.</summary>
    public static string FormatRange(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), zone);
        var c = CultureInfo.InvariantCulture;
        var endText = end.Date == start.Date ? end.ToString("HH:mm", c) : end.ToString("dddd, d MMMM yyyy, HH:mm", c);
        return $"{start.ToString("dddd, d MMMM yyyy, HH:mm", c)}–{endText} ({zone.Id})";
    }

    private static string FormatDate(DateTime startUtc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone)
            .ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private sealed class EmailBody
    {
        private readonly StringBuilder text = new();
        private readonly StringBuilder html = new();
        public string Text => text.ToString();
        public string Html => html.ToString();

        public void Paragraph(string value)
        {
            text.Append(value).Append("\n\n");
            html.Append("<p>").Append(WebUtility.HtmlEncode(value)).Append("</p>");
        }

        public void Field(string label, string value)
        {
            text.Append(label).Append(": ").Append(value).Append('\n');
            html.Append("<p><strong>").Append(WebUtility.HtmlEncode(label)).Append(":</strong> ")
                .Append(WebUtility.HtmlEncode(value)).Append("</p>");
        }

        public void Link(string label, string url)
        {
            text.Append(label).Append(": ").Append(url).Append('\n');
            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">")
                .Append(WebUtility.HtmlEncode(label)).Append("</a></p>");
        }
    }
}
