using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Comments;

public enum CommentStatus { Pending, Approved, Rejected, Spam }

public enum CommentModerationMode { All, AnonymousOnly, None }

public enum BlockedTermAction { Pending, Spam }

/// <summary>Per-App comment configuration. One row per App, created on first save; absent = defaults.</summary>
public class CommentSettings : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public bool Enabled { get; set; }
    public CommentModerationMode ModerationMode { get; set; } = CommentModerationMode.All;
    public bool AllowAnonymous { get; set; } = true;
    public bool RequireEmailForAnonymous { get; set; } = true;
    public int MaxLength { get; set; } = 5000;
    public int MaxDepth { get; set; } = 3;
    public int? AutoCloseAfterDays { get; set; }
    public string[] BlockedTerms { get; set; } = [];
    public BlockedTermAction BlockedTermAction { get; set; } = BlockedTermAction.Pending;
    public int MaxLinks { get; set; } = 2;
    public string[] NotifyEmails { get; set; } = [];
    public bool ExposeAvatarHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;

    /// <summary>Defaults used when an App has never saved settings (not persisted).</summary>
    public static CommentSettings DefaultsFor(Guid appId) => new() { AppId = appId };
}

/// <summary>A comment on a page of a static site. <see cref="Body"/> is plain text, never HTML.</summary>
public class Comment : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }
    public Guid? ParentId { get; set; }
    /// <summary>1 for a top-level comment, parent depth + 1 for a reply.</summary>
    public int Depth { get; set; } = 1;
    public Guid? AppUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>Moderator-only. Never returned by the Client API.</summary>
    public string? AuthorEmail { get; set; }
    public string Body { get; set; } = string.Empty;
    public CommentStatus Status { get; set; } = CommentStatus.Pending;
    public bool IsDeleted { get; set; }
    public string IpHash { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public Guid? ModeratedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public App App { get; set; } = null!;
    public Comment? Parent { get; set; }
    public ICollection<Comment> Replies { get; set; } = [];
    public AppUser? AppUser { get; set; }
    public User? ModeratedBy { get; set; }
}

/// <summary>Per-page overrides (currently: closed for new comments).</summary>
public class CommentPage : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}
