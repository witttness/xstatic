namespace Extatic.Api.Auth;

public interface IPlatformAdminService
{
    /// <summary>True when <paramref name="email"/> is listed in <c>Platform:AdminEmails</c> (case-insensitive).</summary>
    bool IsAdmin(string? email);
}

/// <summary>
/// Reads <c>Platform:AdminEmails</c> as either a string array or a single comma/semicolon-separated
/// string (env-var friendly: <c>Platform__AdminEmails=a@x.com,b@x.com</c>).
/// </summary>
public class PlatformAdminService : IPlatformAdminService
{
    private readonly HashSet<string> admins;

    public PlatformAdminService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Platform:AdminEmails");
        var raw = new List<string>();
        if (!string.IsNullOrWhiteSpace(section.Value)) raw.Add(section.Value);
        raw.AddRange(section.GetChildren().Select(c => c.Value).OfType<string>());
        admins = raw
            .SelectMany(v => v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAdmin(string? email) => !string.IsNullOrWhiteSpace(email) && admins.Contains(email.Trim());
}
