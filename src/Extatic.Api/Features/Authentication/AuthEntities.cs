using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Authentication;

/// <summary>Per-App email/password settings. One row per App, created on first save (defaults otherwise).</summary>
public class AuthSettings : IAppScoped
{
    public const int DefaultPasswordMinLength = 10;

    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public bool RegistrationEnabled { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public int PasswordMinLength { get; set; } = DefaultPasswordMinLength;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    /// <summary>Absolute URL on the tenant's site containing <c>{token}</c>; null → token sent as text.</summary>
    public string? VerifyUrlTemplate { get; set; }
    public string? ResetUrlTemplate { get; set; }
    /// <summary>Lowercase domains allowed to register; null/empty → any domain.</summary>
    public string[]? AllowedEmailDomains { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}

/// <summary>Password credential of a "password"-provider AppUser.</summary>
public class AppUserCredential : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid AppUserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime PasswordChangedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public AppUser AppUser { get; set; } = null!;
}

public static class AuthTokenPurpose
{
    public const string EmailVerification = "EmailVerification";
    public const string PasswordReset = "PasswordReset";
    public const string Refresh = "Refresh";
}

/// <summary>Single-use token (email verification, password reset, refresh). Only the SHA-256 hash is stored.</summary>
public class AppUserAuthToken : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid AppUserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    /// <summary>Refresh rotation: the token issued when this one was used.</summary>
    public Guid? ReplacedByTokenId { get; set; }
    /// <summary>Refresh rotation family (one per login); reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string IpHash { get; set; } = string.Empty;

    public App App { get; set; } = null!;
    public AppUser AppUser { get; set; } = null!;
}
