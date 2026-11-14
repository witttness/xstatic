using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Extatic.Api.Common;

/// <summary>
/// Keyed (salted) SHA-256 of the caller's IP so raw IPs are never stored. The salt is the
/// <c>Jwt:Key</c> configuration value. Use the hash for dedupe / abuse tracking only.
/// </summary>
public static class ClientIps
{
    public static string Hash(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var salt = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required");
        return Hash(context.Connection.RemoteIpAddress, salt);
    }

    public static string Hash(IPAddress? ip, string salt)
    {
        var normalised = ip is null ? "unknown"
            : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();
    }
}
