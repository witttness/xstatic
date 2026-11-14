using System.Text.Json;
using System.Text.RegularExpressions;
using Extatic.Api.Common;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Json.Schema;

namespace Extatic.Api.Features.Forms;

/// <summary>Validation rules for form settings (Platform API create / update).</summary>
public static partial class FormRules
{
    public const int MaxNameLength = 200;
    public const int MaxNotificationEmails = 10;
    public const int MaxRedirectUrlLength = 2048;
    public const int MaxFieldNameLength = 100;
    public const int MaxSchemaBytes = 64 * 1024;

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();

    /// <summary>A validated, normalised copy of the request. Throws ValidationException (422).</summary>
    public record ValidatedForm(
        string Name, string Slug, string? Schema, string[] NotificationEmails,
        string? SuccessRedirectUrl, string? HoneypotField);

    public static ValidatedForm Validate(SaveFormRequest req)
    {
        var errors = new List<ValidationError>();

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            errors.Add(new ValidationError("name", $"Name is required (max {MaxNameLength} characters)"));

        var slug = req.Slug?.Trim();
        if (!Slugs.IsValid(slug))
            errors.Add(new ValidationError("slug", "Must be 1-100 characters: lowercase letters, digits and hyphens"));

        var schema = NormaliseSchema(req.Schema, errors);

        var emails = new List<string>();
        var rawEmails = req.NotificationEmails ?? [];
        if (rawEmails.Length > MaxNotificationEmails)
            errors.Add(new ValidationError("notification_emails", $"At most {MaxNotificationEmails} addresses"));
        for (var i = 0; i < rawEmails.Length; i++)
        {
            var email = rawEmails[i]?.Trim();
            if (!EmailQueue.IsValidAddress(email))
                errors.Add(new ValidationError($"notification_emails/{i}", "Must be a valid email address"));
            else if (!emails.Contains(email!, StringComparer.OrdinalIgnoreCase))
                emails.Add(email!);
        }

        var redirect = string.IsNullOrWhiteSpace(req.SuccessRedirectUrl) ? null : req.SuccessRedirectUrl.Trim();
        if (redirect is not null && !IsAllowedRedirectUrl(redirect))
            errors.Add(new ValidationError("success_redirect_url",
                "Must be an absolute https:// URL (http:// is allowed only for localhost)"));

        var honeypot = string.IsNullOrWhiteSpace(req.HoneypotField) ? null : req.HoneypotField.Trim();
        if (honeypot is not null && !FieldNamePattern().IsMatch(honeypot))
            errors.Add(new ValidationError("honeypot_field",
                "Must be 1-100 characters: letters, digits, '_', '-', '.', ':'"));

        if (errors.Count > 0)
            throw new ValidationException("Invalid form", errors);
        return new ValidatedForm(name!, slug!, schema, emails.ToArray(), redirect, honeypot);
    }

    /// <summary>Absolute https URL, or http to localhost / 127.0.0.1 (local development). No credentials.</summary>
    public static bool IsAllowedRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxRedirectUrlLength) return false;
        if (url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1");
    }

    private static string? NormaliseSchema(JsonElement? schema, List<ValidationError> errors)
    {
        if (schema is null || schema.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (schema.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(new ValidationError("schema", "Schema must be a JSON object"));
            return null;
        }
        var text = schema.Value.GetRawText();
        if (text.Length > MaxSchemaBytes)
        {
            errors.Add(new ValidationError("schema", "Schema is too large (max 64 KB)"));
            return null;
        }
        try
        {
            var built = JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            // Evaluating surfaces problems (e.g. unresolvable $ref) that only appear at evaluation time.
            using var empty = JsonDocument.Parse("{}");
            built.Evaluate(empty.RootElement);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.Add(new ValidationError("schema", $"Invalid JSON Schema: {ex.Message}"));
            return null;
        }
        return text;
    }
}
