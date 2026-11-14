using System.Text.Json;

namespace Extatic.Api.Features.Forms;

/// <summary>Platform view of a Form, with derived counts.</summary>
public record FormDto(
    Guid Id,
    string Name,
    string Slug,
    JsonElement? Schema,
    string[] NotificationEmails,
    string? SuccessRedirectUrl,
    string? HoneypotField,
    bool IsActive,
    bool StoreSubmissions,
    bool RequireAppUser,
    int SubmissionCount,
    int UnreadCount,
    int SpamCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Public (Client API) definition so a site can render / validate client-side.</summary>
public record PublicFormDto(string Name, string Slug, JsonElement? Schema, bool RequireAppUser);

/// <summary>Create (POST) and full replace (PUT) body.</summary>
public record SaveFormRequest(
    string? Name,
    string? Slug,
    JsonElement? Schema,
    string[]? NotificationEmails,
    string? SuccessRedirectUrl,
    string? HoneypotField,
    bool IsActive = true,
    bool StoreSubmissions = true,
    bool RequireAppUser = false);

public record FormSubmissionDto(
    Guid Id,
    Guid FormId,
    JsonElement Data,
    Guid? AppUserId,
    string? UserAgent,
    string? Referrer,
    string Status,
    DateTime CreatedAt);

public record UpdateSubmissionStatusRequest(string? Status);

/// <summary>Response to a JSON submission. <c>Id</c> is null when the form does not store submissions.</summary>
public record SubmissionAcceptedDto(Guid? Id, DateTime CreatedAt);

public record BulkDeleteResult(int Deleted);

public static class FormMappings
{
    public static FormDto ToDto(this Form f, FormCounts counts) =>
        new(f.Id, f.Name, f.Slug, ParseSchema(f.Schema), f.NotificationEmails, f.SuccessRedirectUrl,
            f.HoneypotField, f.IsActive, f.StoreSubmissions, f.RequireAppUser,
            counts.Total, counts.Unread, counts.Spam, f.CreatedAt, f.UpdatedAt);

    public static PublicFormDto ToPublicDto(this Form f) =>
        new(f.Name, f.Slug, ParseSchema(f.Schema), f.RequireAppUser);

    public static FormSubmissionDto ToDto(this FormSubmission s) =>
        new(s.Id, s.FormId, JsonDocument.Parse(s.Data).RootElement.Clone(), s.AppUserId, s.UserAgent,
            s.Referrer, s.Status.ToApiString(), s.CreatedAt);

    public static string ToApiString(this FormSubmissionStatus status) => status.ToString().ToLowerInvariant();

    private static JsonElement? ParseSchema(string? schema)
    {
        if (schema is null) return null;
        using var doc = JsonDocument.Parse(schema);
        return doc.RootElement.Clone();
    }
}

/// <summary>Submission counts for one form. Total excludes Spam.</summary>
public record FormCounts(int Total, int Unread, int Spam)
{
    public static readonly FormCounts Empty = new(0, 0, 0);
}
