using Microsoft.AspNetCore.DataProtection;

namespace Extatic.Api.Security;

/// <summary>ASP.NET Core Data Protection over purpose <see cref="Purpose"/>; key ring persisted in Postgres.</summary>
public class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public const string Purpose = "Extatic.TenantSecrets.v1";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => protector.Unprotect(protectedValue);

    /// <summary>In-memory, process-lifetime keys. For tests only.</summary>
    public static DataProtectionSecretProtector CreateEphemeral()
        => new(new EphemeralDataProtectionProvider());
}
