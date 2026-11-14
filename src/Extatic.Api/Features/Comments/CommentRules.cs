using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Extatic.Api.Services;

namespace Extatic.Api.Features.Comments;

/// <summary>Pure validation and moderation rules for comments.</summary>
public static partial class CommentRules
{
    public const int MaxPageKeyLength = 500;
    public const int MaxPageUrlLength = 2000;
    public const int MaxPageTitleLength = 300;
    public const int MaxAuthorNameLength = 100;
    public const int MaxUserAgentLength = 500;
    public const int AbsoluteMaxLength = 20000;
    public const int AbsoluteMaxDepth = 10;
    public const int MaxBlockedTerms = 500;
    public const int MaxBlockedTermLength = 100;
    public const int MaxNotifyEmails = 10;
    public const int MaxCountKeys = 50;
    public const int MaxBulk = 100;
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);

    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    /// <summary>Control characters other than tab / CR / LF are rejected.</summary>
    public static bool HasForbiddenControlChars(string value)
        => value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));

    /// <summary>Single-line fields (names, keys, titles) allow no control characters at all.</summary>
    public static bool HasAnyControlChars(string value) => value.Any(char.IsControl);

    /// <summary>Trims and validates a page key. Adds an error and returns null when invalid.</summary>
    public static string? NormalisePageKey(string? pageKey, List<ValidationError> errors, string pointer = "page_key")
    {
        var key = pageKey?.Trim();
        if (string.IsNullOrEmpty(key))
            errors.Add(new ValidationError(pointer, "page_key is required"));
        else if (key.Length > MaxPageKeyLength)
            errors.Add(new ValidationError(pointer, $"page_key must be at most {MaxPageKeyLength} characters"));
        else if (HasAnyControlChars(key))
            errors.Add(new ValidationError(pointer, "page_key must not contain control characters"));
        else
            return key;
        return null;
    }

    public static string RequirePageKey(string? pageKey)
    {
        var errors = new List<ValidationError>();
        var key = NormalisePageKey(pageKey, errors);
        if (key is null) throw new ValidationException("Invalid page key", errors);
        return key;
    }

    /// <summary>Validates a comment body against <paramref name="maxLength"/>; returns the trimmed body.</summary>
    public static string? ValidateBody(string? body, int maxLength, List<ValidationError> errors)
    {
        var text = body?.Trim();
        if (string.IsNullOrEmpty(text))
            errors.Add(new ValidationError("body", "body is required"));
        else if (text.Length > maxLength)
            errors.Add(new ValidationError("body", $"body must be at most {maxLength} characters"));
        else if (HasForbiddenControlChars(text))
            errors.Add(new ValidationError("body", "body must not contain control characters"));
        else
            return text;
        return null;
    }

    public static string? ValidateOptionalLine(string? value, int maxLength, string pointer,
        List<ValidationError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > maxLength)
            errors.Add(new ValidationError(pointer, $"{pointer} must be at most {maxLength} characters"));
        else if (HasAnyControlChars(text))
            errors.Add(new ValidationError(pointer, $"{pointer} must not contain control characters"));
        else
            return text;
        return null;
    }

    public static string? ValidatePageUrl(string? value, List<ValidationError> errors)
    {
        var text = ValidateOptionalLine(value, MaxPageUrlLength, "page_url", errors);
        if (text is null) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new ValidationError("page_url", "page_url must be an absolute http(s) URL"));
            return null;
        }
        return text;
    }

    /// <summary>The initial status from the moderation mode, before content downgrades.</summary>
    public static CommentStatus BaseStatus(CommentModerationMode mode, bool signedIn) => mode switch
    {
        CommentModerationMode.None => CommentStatus.Approved,
        CommentModerationMode.AnonymousOnly => signedIn ? CommentStatus.Approved : CommentStatus.Pending,
        _ => CommentStatus.Pending
    };

    /// <summary>
    /// Moderation mode, then downgraded by blocked terms (→ Pending or Spam) and link count (→ Pending).
    /// Never upgrades.
    /// </summary>
    public static CommentStatus DecideStatus(CommentSettings settings, bool signedIn, string body, string authorName)
    {
        var status = BaseStatus(settings.ModerationMode, signedIn);
        if (ContainsBlockedTerm(settings.BlockedTerms, body, authorName))
        {
            if (settings.BlockedTermAction == BlockedTermAction.Spam) return CommentStatus.Spam;
            status = CommentStatus.Pending;
        }
        if (CountLinks(body) > settings.MaxLinks) status = CommentStatus.Pending;
        return status;
    }

    public static bool ContainsBlockedTerm(IEnumerable<string> terms, params string[] texts)
        => terms.Where(t => !string.IsNullOrWhiteSpace(t))
                .Any(term => texts.Any(text => text.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)));

    public static int CountLinks(string body) => LinkPattern().Count(body);

    /// <summary>Gravatar-style avatar hash: SHA-256 hex of the trimmed, lower-cased email.</summary>
    public static string? AvatarHash(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Escapes LIKE wildcards so user search text matches literally.</summary>
    public static string EscapeLike(string value)
        => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
