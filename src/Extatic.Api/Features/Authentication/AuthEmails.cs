using System.Net;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Notifications;

namespace Extatic.Api.Features.Authentication;

/// <summary>Builds the transactional emails. All App/user-controlled text is HTML-encoded in HtmlBody.</summary>
public static class AuthEmails
{
    public const string CategoryVerification = "authentication.verification";
    public const string CategoryReset = "authentication.password_reset";
    public const string CategoryExistingAccount = "authentication.existing_account";

    public static string? BuildLink(string? template, string token)
        => string.IsNullOrEmpty(template) ? null : template.Replace("{token}", Uri.EscapeDataString(token));

    public static EmailMessage Verification(App app, string to, string token, string? template)
        => WithTokenOrLink(app, to, token, BuildLink(template, token),
            subject: $"Verify your email for {app.Name}",
            intro: $"Confirm your email address to finish creating your {app.Name} account.",
            action: "Verify email",
            footer: "This link expires in 24 hours. If you did not sign up, ignore this email.",
            CategoryVerification);

    public static EmailMessage PasswordReset(App app, string to, string token, string? template)
        => WithTokenOrLink(app, to, token, BuildLink(template, token),
            subject: $"Reset your {app.Name} password",
            intro: $"Someone (hopefully you) asked to reset the password of your {app.Name} account.",
            action: "Reset password",
            footer: "This link expires in 1 hour and can be used once. If you did not ask for this, ignore this email.",
            CategoryReset);

    public static EmailMessage ExistingAccount(App app, string to)
    {
        var text = $"Someone tried to create a {app.Name} account with this email address, but you already have one.\n\n"
            + "If this was you, sign in instead, or use \"Forgot password\" if you don't remember your password.\n"
            + "If it was not you, you can ignore this email; your account has not been changed.";
        var html = $"<p>Someone tried to create a {E(app.Name)} account with this email address, but you already have one.</p>"
            + "<p>If this was you, sign in instead, or use “Forgot password” if you don't remember your password.</p>"
            + "<p>If it was not you, you can ignore this email; your account has not been changed.</p>";
        return new EmailMessage(to, $"You already have a {app.Name} account", text, html, null, CategoryExistingAccount);
    }

    private static EmailMessage WithTokenOrLink(App app, string to, string token, string? link,
        string subject, string intro, string action, string footer, string category)
    {
        string text, html;
        if (link is not null)
        {
            text = $"{intro}\n\n{action}: {link}\n\n{footer}";
            html = $"<p>{E(intro)}</p><p><a href=\"{E(link)}\">{E(action)}</a></p>"
                + $"<p>Or paste this link into your browser:<br>{E(link)}</p><p>{E(footer)}</p>";
        }
        else
        {
            text = $"{intro}\n\nYour code: {token}\n\n{footer}";
            html = $"<p>{E(intro)}</p><p>Your code:</p><p><code>{E(token)}</code></p><p>{E(footer)}</p>";
        }
        return new EmailMessage(to, subject, text, html, null, category);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
