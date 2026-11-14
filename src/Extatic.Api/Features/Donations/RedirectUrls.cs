namespace Extatic.Api.Features.Donations;

/// <summary>Success / cancel URL rules. Only campaign-configured URLs are ever sent to the provider.</summary>
public static class RedirectUrls
{
    public const int MaxLength = 2000;

    /// <summary>Absolute https URL, or http on localhost / 127.0.0.1 (local development). No credentials.</summary>
    public static bool IsValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxLength) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return !string.IsNullOrEmpty(uri.Host);
        return uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == "localhost");
    }

    /// <summary>Adds <c>donation_id=&lt;id&gt;</c> to the query string, preserving any fragment.</summary>
    public static string WithDonationId(string url, Guid donationId)
    {
        var builder = new UriBuilder(url);
        var query = builder.Query.TrimStart('?');
        var param = $"donation_id={donationId}";
        builder.Query = string.IsNullOrEmpty(query) ? param : $"{query}&{param}";
        return builder.Uri.AbsoluteUri; // default ports are omitted
    }
}
