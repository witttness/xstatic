using System.Security.Cryptography;
using System.Text;

namespace Extatic.Api.Common;

public static class Tokens
{
    /// <summary>Cryptographically random, URL-safe base64 (no padding). Default 32 bytes → 43 chars.</summary>
    public static string NewUrlToken(int bytes = 32)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 lowercase hex of the UTF-8 bytes. Store this, never the raw token.</summary>
    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Constant-time comparison of two hashes / tokens.</summary>
    public static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
