using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Forms;

/// <summary>A public form a static site posts to. Submission counts are derived, never stored.</summary>
public class Form : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    /// <summary>Optional JSON Schema (draft 2020-12) stored as jsonb.</summary>
    public string? Schema { get; set; }
    public string[] NotificationEmails { get; set; } = [];
    /// <summary>Validated on save: absolute https URL, or http://localhost[:port].</summary>
    public string? SuccessRedirectUrl { get; set; }
    /// <summary>Name of a field that must be empty; when filled the submission is treated as spam.</summary>
    public string? HoneypotField { get; set; }
    public bool IsActive { get; set; } = true;
    public bool StoreSubmissions { get; set; } = true;
    public bool RequireAppUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public ICollection<FormSubmission> Submissions { get; set; } = [];
}

public enum FormSubmissionStatus
{
    New,
    Read,
    Spam,
    Archived
}

public class FormSubmission : IAppScoped
{
    public const int MaxUserAgentLength = 512;
    public const int MaxReferrerLength = 2048;

    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid FormId { get; set; }
    public string Data { get; set; } = "{}";
    public Guid? AppUserId { get; set; }
    /// <summary>Keyed hash of the client IP (ClientIps.Hash) — raw IPs are never stored.</summary>
    public string IpHash { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public FormSubmissionStatus Status { get; set; } = FormSubmissionStatus.New;
    public DateTime CreatedAt { get; set; }

    public App App { get; set; } = null!;
    public Form Form { get; set; } = null!;
    public AppUser? AppUser { get; set; }
}
