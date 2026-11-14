using System.Net;
using Extatic.Api.Notifications;

namespace Extatic.Api.Features.Members;

/// <summary>
/// Membership email templates. Every interpolated value (app/plan/member names, reasons, member
/// numbers) is HTML-encoded in the HTML body.
/// </summary>
public static class MemberEmails
{
    public const string CategoryPrefix = "members.";

    public sealed record Context(string AppName, string PlanName, string? DisplayName, string? MemberNumber,
        DateTime? ExpiresAt, string? Reason);

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Greeting(Context c) => string.IsNullOrWhiteSpace(c.DisplayName) ? "Hello," : $"Hello {c.DisplayName},";

    private static string Expiry(DateTime? d) => d is null ? "does not expire" : $"is valid until {d.Value:yyyy-MM-dd}";

    private static EmailMessage Build(string to, string category, string subject, Context c, params string[] lines)
    {
        var reasonLine = string.IsNullOrWhiteSpace(c.Reason) ? null : $"Reason: {c.Reason}";
        var all = lines.Append(reasonLine).Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToArray();
        var text = Greeting(c) + "\n\n" + string.Join("\n\n", all) + $"\n\n— {c.AppName}\n";
        var html = $"<p>{H(Greeting(c))}</p>" + string.Concat(all.Select(l => $"<p>{H(l)}</p>")) + $"<p>— {H(c.AppName)}</p>";
        return new EmailMessage(to, subject, text, html, null, CategoryPrefix + category);
    }

    public static EmailMessage ApplicationReceived(string to, Context c) => Build(to, "application_received",
        $"{c.AppName}: we received your {c.PlanName} membership application", c,
        $"Thanks for applying for the {c.PlanName} membership. We'll let you know once it has been reviewed.");

    public static EmailMessage Welcome(string to, Context c) => Build(to, "approved",
        $"Welcome to {c.AppName} — your {c.PlanName} membership is active", c,
        $"Your {c.PlanName} membership is now active.",
        c.MemberNumber is null ? "" : $"Your member number is {c.MemberNumber}.",
        $"Your membership {Expiry(c.ExpiresAt)}.");

    public static EmailMessage Rejected(string to, Context c) => Build(to, "rejected",
        $"{c.AppName}: your {c.PlanName} membership application", c,
        $"Unfortunately your application for the {c.PlanName} membership was not approved.");

    public static EmailMessage Suspended(string to, Context c) => Build(to, "suspended",
        $"{c.AppName}: your membership has been suspended", c,
        $"Your {c.PlanName} membership has been suspended.");

    public static EmailMessage Reinstated(string to, Context c) => Build(to, "reinstated",
        $"{c.AppName}: your membership has been reinstated", c,
        $"Your {c.PlanName} membership is active again. It {Expiry(c.ExpiresAt)}.");

    public static EmailMessage RenewalReminder(string to, Context c, int days) => Build(to, "renewal_reminder",
        $"{c.AppName}: your membership expires in {days} day{(days == 1 ? "" : "s")}", c,
        $"Your {c.PlanName} membership expires on {c.ExpiresAt:yyyy-MM-dd}. Renew it to keep your benefits.");

    public static EmailMessage Expired(string to, Context c) => Build(to, "expired",
        $"{c.AppName}: your membership has expired", c,
        $"Your {c.PlanName} membership has expired. Renew it to regain access.");

    public static EmailMessage Cancelled(string to, Context c) => Build(to, "cancelled",
        $"{c.AppName}: your membership has been cancelled", c,
        $"Your {c.PlanName} membership has been cancelled.");

    public static EmailMessage NewApplicationNotice(string to, Context c, string? email, bool pending) => Build(to,
        "new_application",
        pending ? $"{c.AppName}: new {c.PlanName} membership application" : $"{c.AppName}: new {c.PlanName} member",
        c with { DisplayName = null, Reason = null },
        pending
            ? $"{c.DisplayName ?? email ?? "Someone"} ({email ?? "no email"}) applied for the {c.PlanName} membership and is awaiting approval."
            : $"{c.DisplayName ?? email ?? "Someone"} ({email ?? "no email"}) joined the {c.PlanName} membership.");
}
