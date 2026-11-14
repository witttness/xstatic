namespace Extatic.Api.Security;

/// <summary>
/// Encrypts tenant-supplied secrets (OAuth client secrets, payment provider keys, ...) for storage
/// at rest. Store only the output of <see cref="Protect"/>; never log either value.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> if tampered or the key is gone.</summary>
    string Unprotect(string protectedValue);
}
