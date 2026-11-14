using System.Text.RegularExpressions;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Authentication;

public partial class AuthSettingsService(AppDbContext db)
{
    public const int MaxAllowedDomains = 100;

    /// <summary>The App's settings, or unsaved defaults when no row exists yet (lazy creation).</summary>
    public async Task<(AuthSettings Settings, bool Persisted)> GetAsync(Guid appId)
    {
        var settings = await db.AuthSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AppId == appId);
        return settings is null ? (new AuthSettings { AppId = appId }, false) : (settings, true);
    }

    public async Task<AuthSettings> UpdateAsync(App app, UpdateAuthSettingsRequest req)
    {
        var normalised = Validate(app, req);
        for (var attempt = 0; ; attempt++)
        {
            var settings = await db.AuthSettings.FirstOrDefaultAsync(s => s.AppId == app.Id);
            if (settings is null)
            {
                settings = new AuthSettings { AppId = app.Id };
                db.AuthSettings.Add(settings);
            }
            Apply(settings, normalised);
            try
            {
                await db.SaveChangesAsync();
                return settings;
            }
            catch (DbUpdateException ex) when (attempt == 0
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two first-time saves raced on the unique AppId index: retry as an update.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static void Apply(AuthSettings target, UpdateAuthSettingsRequest source)
    {
        target.RegistrationEnabled = source.RegistrationEnabled;
        target.RequireEmailVerification = source.RequireEmailVerification;
        target.PasswordMinLength = source.PasswordMinLength;
        target.AccessTokenMinutes = source.AccessTokenMinutes;
        target.RefreshTokenDays = source.RefreshTokenDays;
        target.MaxFailedAttempts = source.MaxFailedAttempts;
        target.LockoutMinutes = source.LockoutMinutes;
        target.VerifyUrlTemplate = source.VerifyUrlTemplate;
        target.ResetUrlTemplate = source.ResetUrlTemplate;
        target.AllowedEmailDomains = source.AllowedEmailDomains is { Length: > 0 } d ? d : null;
    }

    /// <summary>Validates and returns a normalised copy (trimmed templates, lowercase de-duplicated domains).</summary>
    public static UpdateAuthSettingsRequest Validate(App app, UpdateAuthSettingsRequest req)
    {
        var errors = new List<ValidationError>();
        void Range(int value, int min, int max, string pointer)
        {
            if (value < min || value > max)
                errors.Add(new ValidationError(pointer, $"Must be between {min} and {max}"));
        }
        Range(req.PasswordMinLength, PasswordPolicy.AbsoluteMinLength, 128, "password_min_length");
        Range(req.AccessTokenMinutes, 5, 1440, "access_token_minutes");
        Range(req.RefreshTokenDays, 1, 365, "refresh_token_days");
        Range(req.MaxFailedAttempts, 1, 100, "max_failed_attempts");
        Range(req.LockoutMinutes, 1, 1440, "lockout_minutes");

        var verify = ValidateTemplate(app, req.VerifyUrlTemplate, "verify_url_template", errors);
        var reset = ValidateTemplate(app, req.ResetUrlTemplate, "reset_url_template", errors);

        var domains = new List<string>();
        foreach (var raw in req.AllowedEmailDomains ?? [])
        {
            var domain = raw?.Trim().TrimStart('@').ToLowerInvariant() ?? "";
            if (!DomainRegex().IsMatch(domain))
            {
                errors.Add(new ValidationError("allowed_email_domains", $"'{raw}' is not a valid domain"));
                continue;
            }
            if (!domains.Contains(domain)) domains.Add(domain);
        }
        if (domains.Count > MaxAllowedDomains)
            errors.Add(new ValidationError("allowed_email_domains", $"At most {MaxAllowedDomains} domains"));

        if (errors.Count > 0) throw new ValidationException("Invalid authentication settings", errors);
        return req with { VerifyUrlTemplate = verify, ResetUrlTemplate = reset, AllowedEmailDomains = [.. domains] };
    }

    /// <summary>
    /// Absolute https URL (or http on localhost) containing <c>{token}</c>, without credentials. When the App
    /// has allowed origins configured, the template's origin must be one of them (or localhost).
    /// </summary>
    public static string? ValidateTemplate(App app, string? template, string pointer, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        template = template.Trim();
        if (template.Length > 2048)
        {
            errors.Add(new ValidationError(pointer, "Must be at most 2048 characters"));
            return null;
        }
        if (!template.Contains("{token}"))
        {
            errors.Add(new ValidationError(pointer, "Must contain the {token} placeholder"));
            return null;
        }
        var probe = template.Replace("{token}", "TOKEN");
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || probe.Any(char.IsWhiteSpace) || probe.Any(char.IsControl))
        {
            errors.Add(new ValidationError(pointer, "Must be an absolute URL"));
            return null;
        }
        var isLocalhost = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLocalhost))
        {
            errors.Add(new ValidationError(pointer, "Must use https (http is allowed only for localhost)"));
            return null;
        }
        var origins = app.AllowedOrigins ?? [];
        if (!isLocalhost && origins.Length > 0)
        {
            var origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            if (!origins.Any(o => string.Equals(o.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(new ValidationError(pointer, "Must be on one of the App's allowed origins"));
                return null;
            }
        }
        return template;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainRegex();
}
