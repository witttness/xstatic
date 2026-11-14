using System.Text.RegularExpressions;
using Extatic.Api.Services;

namespace Extatic.Api.Common;

public static partial class Slugs
{
    public const int MaxLength = 100;

    [GeneratedRegex("^[a-z0-9-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Lowercase ASCII letters, digits and hyphens; 1–100 characters.</summary>
    public static bool IsValid(string? slug) => slug is not null && Pattern().IsMatch(slug);

    /// <summary>Throws <see cref="ValidationException"/> (422) with <paramref name="pointer"/> when invalid.</summary>
    public static void EnsureValid(string? slug, string pointer = "slug")
    {
        if (!IsValid(slug))
            throw new ValidationException("Invalid slug",
                [new ValidationError(pointer, "Must be 1-100 characters: lowercase letters, digits and hyphens")]);
    }
}
