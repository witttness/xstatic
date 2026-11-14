using System.Text.Json;
using Extatic.Api.Services;

namespace Extatic.Api.Features.Comments;

// ---- Client API -------------------------------------------------------------------------------

/// <summary><c>website</c> is a honeypot: real visitors never see or fill it.</summary>
public record CreateCommentRequest(
    string? PageKey,
    string? PageUrl,
    string? PageTitle,
    Guid? ParentId,
    string? AuthorName,
    string? AuthorEmail,
    string? Body,
    string? Website);

public record UpdateCommentRequest(string? Body);

/// <summary>A comment as shown publicly. Deliberately has no email field.</summary>
public record PublicCommentDto(
    Guid Id,
    Guid? ParentId,
    string? AuthorName,
    string? AvatarHash,
    string? Body,
    bool IsDeleted,
    bool Pending,
    bool IsOwn,
    DateTime CreatedAt,
    DateTime? EditedAt,
    List<PublicCommentDto> Replies);

/// <summary>Top-level comments are paged (<see cref="Total"/> counts them); replies are nested.</summary>
public record CommentThreadDto(
    string PageKey,
    bool Enabled,
    bool IsClosed,
    int Total,
    int ApprovedCount,
    int Page,
    int PageSize,
    List<PublicCommentDto> Items);

/// <summary>The author's view of a comment they just posted or edited. No email field.</summary>
public record PostedCommentDto(
    Guid Id,
    Guid? ParentId,
    string PageKey,
    string AuthorName,
    string Body,
    string Status,
    DateTime CreatedAt,
    DateTime? EditedAt);

public record CommentCountsDto(Dictionary<string, int> Counts);

// ---- Platform API -----------------------------------------------------------------------------

public record CommentAdminDto(
    Guid Id,
    string PageKey,
    string? PageUrl,
    string? PageTitle,
    Guid? ParentId,
    int Depth,
    Guid? AppUserId,
    string AuthorName,
    string? AuthorEmail,
    string Body,
    string Status,
    bool IsDeleted,
    string? UserAgent,
    DateTime CreatedAt,
    DateTime? EditedAt,
    DateTime? ModeratedAt,
    Guid? ModeratedByUserId);

public record BulkModerateRequest(List<Guid>? Ids, string? Action);

public record BulkModerateResult(int Updated, int NotFound);

public record CommentSettingsDto(
    bool Enabled,
    string ModerationMode,
    bool AllowAnonymous,
    bool RequireEmailForAnonymous,
    int MaxLength,
    int MaxDepth,
    int? AutoCloseAfterDays,
    string[] BlockedTerms,
    string BlockedTermAction,
    int MaxLinks,
    string[] NotifyEmails,
    bool ExposeAvatarHash,
    DateTime? UpdatedAt);

/// <summary>Full replacement of the App's comment settings.</summary>
public record UpdateCommentSettingsRequest(
    bool Enabled,
    string? ModerationMode,
    bool AllowAnonymous,
    bool RequireEmailForAnonymous,
    int MaxLength,
    int MaxDepth,
    int? AutoCloseAfterDays,
    string[]? BlockedTerms,
    string? BlockedTermAction,
    int MaxLinks,
    string[]? NotifyEmails,
    bool ExposeAvatarHash);

public record CommentPageDto(
    string PageKey,
    string? PageUrl,
    string? PageTitle,
    bool IsClosed,
    int Total,
    int Approved,
    int Pending,
    DateTime? LastCommentAt);

public record PageKeyRequest(string? PageKey);

public record CommentStatsDto(int Pending, int Approved, int Rejected, int Spam);

public static class CommentMappings
{
    public static string ToApi<TEnum>(this TEnum value) where TEnum : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static TEnum ParseApi<TEnum>(string? value, string pointer) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
            if (string.Equals(candidate.ToApi(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
                return candidate;
        var allowed = string.Join(", ", Enum.GetValues<TEnum>().Select(v => v.ToApi()));
        throw new ValidationException($"Invalid {pointer}",
            [new ValidationError(pointer, $"Must be one of: {allowed}")]);
    }

    public static PostedCommentDto ToPostedDto(this Comment c) => new(
        c.Id, c.ParentId, c.PageKey, c.AuthorName, c.Body, c.Status.ToApi(), c.CreatedAt, c.EditedAt);

    public static CommentAdminDto ToAdminDto(this Comment c) => new(
        c.Id, c.PageKey, c.PageUrl, c.PageTitle, c.ParentId, c.Depth, c.AppUserId, c.AuthorName,
        c.AuthorEmail, c.Body, c.Status.ToApi(), c.IsDeleted, c.UserAgent, c.CreatedAt, c.EditedAt,
        c.ModeratedAt, c.ModeratedByUserId);

    public static CommentSettingsDto ToDto(this CommentSettings s) => new(
        s.Enabled, s.ModerationMode.ToApi(), s.AllowAnonymous, s.RequireEmailForAnonymous, s.MaxLength,
        s.MaxDepth, s.AutoCloseAfterDays, s.BlockedTerms, s.BlockedTermAction.ToApi(), s.MaxLinks,
        s.NotifyEmails, s.ExposeAvatarHash, s.Id == Guid.Empty ? null : s.UpdatedAt);
}
