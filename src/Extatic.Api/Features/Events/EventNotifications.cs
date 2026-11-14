using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Notifications;
using Extatic.Api.Webhooks;

namespace Extatic.Api.Features.Events;

/// <summary>
/// Stages emails (IEmailQueue) and webhooks (WebhookDispatcher) on the caller's DbContext. Nothing is
/// saved here: the caller's SaveChanges/transaction makes them atomic with the registration change.
/// </summary>
public class EventNotifications(IEmailQueue emailQueue, WebhookDispatcher webhooks)
{
    public const string RegistrationCreated = "event.registration.created";
    public const string RegistrationWaitlisted = "event.registration.waitlisted";
    public const string RegistrationPromoted = "event.registration.promoted";
    public const string RegistrationCancelled = "event.registration.cancelled";
    public const string EventCancelled = "event.cancelled";

    public static readonly IReadOnlyList<string> WebhookEvents =
        [RegistrationCreated, RegistrationWaitlisted, RegistrationPromoted, RegistrationCancelled, EventCancelled];

    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>New registration: confirmation or waitlisted email (with manage link/token), notify list, webhook.</summary>
    public async Task RegisteredAsync(AppDbContext db, Event ev, EventRegistration r, string manageToken)
    {
        var waitlisted = r.Status == EventRegistrationStatus.Waitlisted;
        var subject = waitlisted ? $"You're on the waitlist: {ev.Title}" : $"Registration confirmed: {ev.Title}";
        var intro = waitlisted
            ? $"You're on the waitlist for {ev.Title} ({Seats(r.Seats)}). We'll email you if a place opens up."
            : $"You're registered for {ev.Title} ({Seats(r.Seats)}).";
        Stage(db, ev, r.Email, subject, intro, ManageLines(ev, manageToken),
            waitlisted ? "events.waitlisted" : "events.confirmation");

        foreach (var to in ev.NotifyEmails)
        {
            var notice = $"New registration for {ev.Title}: {r.Name} <{r.Email}>, {Seats(r.Seats)}, status {EventRules.StatusName(r.Status)}.";
            Stage(db, ev, to, $"New registration: {ev.Title}", notice, [], "events.notification");
        }

        await StageWebhookAsync(db, ev, waitlisted ? RegistrationWaitlisted : RegistrationCreated, r);
    }

    public async Task PromotedAsync(AppDbContext db, Event ev, EventRegistration r)
    {
        Stage(db, ev, r.Email, $"A place opened up: {ev.Title}",
            $"Good news: you've been moved off the waitlist and are now registered for {ev.Title} ({Seats(r.Seats)}).",
            ["Use the manage link from your original registration email to view or cancel your registration."],
            "events.promoted");
        await StageWebhookAsync(db, ev, RegistrationPromoted, r);
    }

    public async Task CancelledAsync(AppDbContext db, Event ev, EventRegistration r)
    {
        Stage(db, ev, r.Email, $"Registration cancelled: {ev.Title}",
            $"Your registration for {ev.Title} has been cancelled.", [], "events.cancellation");
        await StageWebhookAsync(db, ev, RegistrationCancelled, r);
    }

    public async Task EventCancelledAsync(AppDbContext db, Event ev, IReadOnlyList<EventRegistration> active)
    {
        foreach (var r in active)
            Stage(db, ev, r.Email, $"Event cancelled: {ev.Title}",
                $"We're sorry: {ev.Title} has been cancelled. Your registration no longer applies.", [],
                "events.event_cancelled");
        var data = JsonSerializer.SerializeToElement(new
        {
            id = ev.Id,
            slug = ev.Slug,
            title = ev.Title,
            starts_at = ev.StartsAt,
            active_registrations = active.Count
        }, SnakeCase);
        await webhooks.StageAsync(db, ev.AppId, EventCancelled, data);
    }

    private Task StageWebhookAsync(AppDbContext db, Event ev, string type, EventRegistration r)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            id = r.Id,
            event_id = ev.Id,
            event_slug = ev.Slug,
            name = r.Name,
            email = r.Email,
            seats = r.Seats,
            status = EventRules.StatusName(r.Status),
            data = JsonDocument.Parse(r.Data).RootElement,
            app_user_id = r.AppUserId,
            created_at = r.CreatedAt,
            confirmed_at = r.ConfirmedAt,
            promoted_at = r.PromotedAt,
            cancelled_at = r.CancelledAt
        }, SnakeCase);
        return webhooks.StageAsync(db, ev.AppId, type, data);
    }

    private static string Seats(int n) => n == 1 ? "1 seat" : $"{n} seats";

    private static List<string> ManageLines(Event ev, string token)
        => ev.ManageUrlTemplate is { } template
            ? [$"Manage or cancel your registration: {template.Replace("{token}", Uri.EscapeDataString(token))}"]
            : [$"Your registration reference (keep it to manage or cancel your registration): {token}"];

    public static string FormatWhen(Event ev)
    {
        var tz = EventRules.FindTimezone(ev.Timezone);
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc), tz);
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.EndsAt, DateTimeKind.Utc), tz);
        var c = CultureInfo.InvariantCulture;
        var endText = start.Date == end.Date ? end.ToString("HH:mm", c) : end.ToString("ddd d MMM yyyy HH:mm", c);
        return $"{start.ToString("ddd d MMM yyyy HH:mm", c)} - {endText} ({ev.Timezone})";
    }

    private void Stage(AppDbContext db, Event ev, string to, string subject, string intro,
        IReadOnlyList<string> extra, string category)
    {
        var lines = new List<string> { intro, "", $"When: {FormatWhen(ev)}" };
        if (ev.Location is not null) lines.Add($"Where: {ev.Location}");
        if (extra.Count > 0) { lines.Add(""); lines.AddRange(extra); }
        var text = string.Join("\n", lines);

        var html = new StringBuilder();
        foreach (var line in lines)
            html.Append(line.Length == 0 ? "<br>" : $"<p>{Linkify(line)}</p>");
        emailQueue.Stage(db, ev.AppId, new EmailMessage(to, subject, text, html.ToString(), null, category));
    }

    /// <summary>HTML-encodes the line; an https manage URL at the end becomes a link.</summary>
    private static string Linkify(string line)
    {
        var idx = line.IndexOf("https://", StringComparison.Ordinal);
        if (idx < 0 || line.IndexOf(' ', idx) >= 0) return WebUtility.HtmlEncode(line);
        var url = line[idx..];
        return WebUtility.HtmlEncode(line[..idx])
               + $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a>";
    }
}
