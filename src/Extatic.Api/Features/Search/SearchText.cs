using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Extatic.Api.Features.Search;

public static class SearchLimits
{
    public const int MaxDocumentsPerPush = 500;
    public const int MaxIdsPerRequest = 1000;
    public const int MaxBodyBytes = 200 * 1024;
    public const int MaxRawBodyChars = 2 * 1024 * 1024;
    public const int MaxTitleLength = 500;
    public const int MaxSummaryLength = 2000;
    public const int MaxTags = 50;
    public const int MaxTagLength = 100;
    public const int MaxSectionLength = 100;
    public const int MaxExternalIdLength = 512;
    public const int MaxUrlLength = 2048;
    public const int MaxUrlTemplateLength = 500;
    public const int MaxPointerLength = 200;
    public const int MaxBodyPointers = 10;
    public const int MaxMetadataBytes = 8 * 1024;
    public const int MaxQueryLength = 200;
    public const int MaxPageSize = 50;
    public const int MaxPage = 1000;
    public const int MaxSuggestions = 8;
    public const int MaxDocumentsCeiling = 100_000;
}

/// <summary>Allowlisted Postgres text-search configurations (identifiers are never taken from input unchecked).</summary>
public static class SearchLanguages
{
    public const string Default = "english";

    public static readonly IReadOnlyList<string> All =
    [
        "simple", "arabic", "armenian", "basque", "catalan", "danish", "dutch", "english", "estonian",
        "finnish", "french", "german", "greek", "hindi", "hungarian", "indonesian", "irish", "italian",
        "lithuanian", "nepali", "norwegian", "portuguese", "romanian", "russian", "serbian", "spanish",
        "swedish", "tamil", "turkish", "yiddish"
    ];

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool IsValid(string? language) => language is not null && Set.Contains(language);
}

/// <summary>Text normalisation shared by indexing and querying.</summary>
public static class SearchText
{
    /// <summary>Private-use code points used as ts_headline StartSel/StopSel. Removed from all indexed text.</summary>
    public const char MarkStart = '';
    public const char MarkEnd = '';

    /// <summary>
    /// Removes characters Postgres cannot store or that would break snippet rendering: NUL and other
    /// control characters (whitespace controls become spaces), unpaired surrogates, and the
    /// highlight sentinels. Collapses runs of whitespace and trims.
    /// </summary>
    public static string Clean(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        var pendingSpace = false;
        foreach (var rune in input.EnumerateRunes())
        {
            // EnumerateRunes yields U+FFFD for an unpaired surrogate, which is valid UTF-8.
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (rune.Value is MarkStart or MarkEnd) continue;
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    /// <summary>Truncates to at most <paramref name="maxBytes"/> UTF-8 bytes without splitting a character.</summary>
    public static string TruncateUtf8(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes) break;
            sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    public static string Truncate(string text, int maxChars)
        => text.Length <= maxChars ? text : TruncateUtf16Safe(text, maxChars);

    private static string TruncateUtf16Safe(string text, int maxChars)
    {
        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut];
    }

    private static readonly HashSet<string> SkippedElements =
        new(["script", "style", "noscript", "template", "head"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Simple, safe HTML-to-text: drops comments, tags and the content of script/style/noscript/
    /// template/head, decodes entities and cleans whitespace. A '&lt;' not followed by a tag-like
    /// character ("a &lt; b") is kept. Linear time, no regex backtracking. The output is plain text
    /// and is HTML-escaped again when rendered in snippets.
    /// </summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var sb = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c != '<' || i + 1 >= html.Length)
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                sb.Append(' ');
                continue;
            }
            var next = html[i + 1];
            if (!(char.IsAsciiLetter(next) || next == '/' || next == '!' || next == '?'))
            {
                sb.Append(c);
                i++;
                continue;
            }
            var tagEnd = FindTagEnd(html, i + 1);
            var name = TagName(html, i + 1, tagEnd);
            i = tagEnd < 0 ? html.Length : tagEnd + 1;
            sb.Append(' ');
            if (next != '/' && SkippedElements.Contains(name))
            {
                var close = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                if (close < 0) { i = html.Length; break; }
                var closeEnd = FindTagEnd(html, close + 1);
                i = closeEnd < 0 ? html.Length : closeEnd + 1;
            }
        }
        return Clean(WebUtility.HtmlDecode(sb.ToString()));
    }

    private static int FindTagEnd(string html, int start)
    {
        char quote = '\0';
        for (var j = start; j < html.Length; j++)
        {
            var ch = html[j];
            if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
            if (ch is '"' or '\'') quote = ch;
            else if (ch == '>') return j;
        }
        return -1;
    }

    private static string TagName(string html, int start, int end)
    {
        var j = start;
        if (j < html.Length && html[j] == '/') j++;
        var k = j;
        var limit = end < 0 ? html.Length : end;
        while (k < limit && (char.IsAsciiLetterOrDigit(html[k]) || html[k] == '-')) k++;
        return html[j..k];
    }

    /// <summary>Normalised form for the query log: cleaned, lower-case, truncated.</summary>
    public static string NormalizeForLog(string query)
        => Truncate(Clean(query).ToLowerInvariant(), SearchQueryLog.MaxQueryLength);

    /// <summary>
    /// Converts a ts_headline result (plain text with <see cref="MarkStart"/>/<see cref="MarkEnd"/>
    /// sentinels) to HTML: every text segment is HTML-encoded and only &lt;mark&gt; tags are added.
    /// </summary>
    public static string RenderSnippet(string? headline)
    {
        if (string.IsNullOrEmpty(headline)) return string.Empty;
        var sb = new StringBuilder(headline.Length + 32);
        var segment = new StringBuilder();
        var open = false;
        foreach (var c in headline)
        {
            if (c == MarkStart || c == MarkEnd)
            {
                sb.Append(WebUtility.HtmlEncode(segment.ToString()));
                segment.Clear();
                if (c == MarkStart && !open) { sb.Append("<mark>"); open = true; }
                else if (c == MarkEnd && open) { sb.Append("</mark>"); open = false; }
                continue;
            }
            segment.Append(c);
        }
        sb.Append(WebUtility.HtmlEncode(segment.ToString()));
        if (open) sb.Append("</mark>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a to_tsquery('simple', …) expression for title completion: up to 6 words of
    /// letters/digits (everything else is a separator, so no tsquery operator can be injected), the
    /// last one as a prefix (<c>:*</c>). Returns null when nothing usable remains.
    /// </summary>
    public static string? BuildPrefixQuery(string input)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (var rune in Clean(input).ToLowerInvariant().EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            var isWordChar = Rune.IsLetterOrDigit(rune) || cat is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark;
            if (isWordChar) { current.Append(rune.ToString()); continue; }
            Flush();
        }
        Flush();
        if (terms.Count == 0) return null;
        var take = terms.TakeLast(6).ToList();
        var parts = take.Select((t, i) => i == take.Count - 1 ? $"'{t}':*" : $"'{t}'");
        return string.Join(" & ", parts);

        void Flush()
        {
            if (current.Length > 0) terms.Add(Truncate(current.ToString(), 64));
            current.Clear();
        }
    }

    /// <summary>Lower-cased, trimmed, de-duplicated tags (empty ones dropped).</summary>
    public static string[] NormalizeTags(IEnumerable<string?>? tags)
        => tags is null
            ? []
            : tags.Select(t => Clean(t).ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    /// <summary>A relative path ("/x", not "//x") or an absolute http(s) URL.</summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > SearchLimits.MaxUrlLength) return false;
        if (url.Any(char.IsControl) || url.Contains('\\')) return false;
        if (url.StartsWith('/')) return !url.StartsWith("//", StringComparison.Ordinal);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

/// <summary>Extracts searchable text from Item.data (RFC 6901 JSON pointers) and expands URL templates.</summary>
public static partial class ItemTextExtractor
{
    [GeneratedRegex(@"\{([A-Za-z0-9_]{1,64})\}")]
    private static partial Regex PlaceholderRegex();

    public static bool IsValidPointer(string? pointer)
        => pointer is not null && pointer.Length <= SearchLimits.MaxPointerLength
           && (pointer.Length == 0 || pointer[0] == '/')
           && !pointer.Any(char.IsControl);

    public static JsonElement? Resolve(JsonElement root, string? pointer)
    {
        if (pointer is null) return null;
        if (pointer.Length == 0) return root;
        if (pointer[0] != '/') return null;
        var current = root;
        foreach (var raw in pointer[1..].Split('/'))
        {
            var token = raw.Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out current)) return null;
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    || (token.Length > 1 && token[0] == '0')
                    || index >= current.GetArrayLength())
                    return null;
                current = current[index];
            }
            else return null;
        }
        return current;
    }

    /// <summary>A string, or the strings of an array (nested arrays flattened); anything else yields nothing.</summary>
    public static IEnumerable<string> Strings(JsonElement? element)
    {
        if (element is not { } e) yield break;
        if (e.ValueKind == JsonValueKind.String)
        {
            yield return e.GetString() ?? string.Empty;
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in e.EnumerateArray())
                foreach (var s in Strings(child))
                    yield return s;
        }
    }

    public static string Text(JsonElement root, string? pointer)
        => string.Join(" ", Strings(Resolve(root, pointer)));

    public static bool IsValidUrlTemplate(string? template)
    {
        if (template is null) return true;
        if (template.Length == 0 || template.Length > SearchLimits.MaxUrlTemplateLength) return false;
        var withoutPlaceholders = PlaceholderRegex().Replace(template, "x");
        if (withoutPlaceholders.Contains('{') || withoutPlaceholders.Contains('}')) return false;
        return SearchText.IsSafeUrl(withoutPlaceholders);
    }

    /// <summary>
    /// Expands <c>{field}</c> (top-level string/number in data) and <c>{id}</c>; values are
    /// URL-encoded. Returns null when a placeholder has no usable value.
    /// </summary>
    public static string? ExpandUrl(string? template, Guid itemId, JsonElement data)
    {
        if (string.IsNullOrEmpty(template)) return null;
        var missing = false;
        var url = PlaceholderRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (name == "id") return itemId.ToString();
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value))
            {
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrEmpty(text)) return Uri.EscapeDataString(text);
            }
            missing = true;
            return string.Empty;
        });
        return missing || !SearchText.IsSafeUrl(url) ? null : url;
    }
}
