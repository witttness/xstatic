using System.Text.Json;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Authentication;

// ---- Client API ----

public record RegisterRequest(string? Email, string? Password, string? DisplayName);
public record EmailRequest(string? Email);
public record TokenRequest(string? Token);
public record LoginRequest(string? Email, string? Password);
public record RefreshRequest(string? RefreshToken);
public record ResetPasswordRequest(string? Token, string? Password);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public record DeleteAccountRequest(string? Password);
public record UpdateProfileRequest(string? DisplayName, string? AvatarUrl, JsonElement? Metadata);

/// <summary>Deliberately identical for every outcome of register / resend / forgot-password.</summary>
public record AcceptedResponse(string Message);

public record EmailVerifiedResponse(bool EmailVerified);

public record AuthUserDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    string? AvatarUrl,
    JsonElement? Metadata,
    bool EmailVerified,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public record AuthTokensResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, AuthUserDto User);

// ---- Platform API ----

public record AuthSettingsDto(
    bool RegistrationEnabled,
    bool RequireEmailVerification,
    int PasswordMinLength,
    int AccessTokenMinutes,
    int RefreshTokenDays,
    int MaxFailedAttempts,
    int LockoutMinutes,
    string? VerifyUrlTemplate,
    string? ResetUrlTemplate,
    string[] AllowedEmailDomains,
    DateTime? UpdatedAt);

public record UpdateAuthSettingsRequest(
    bool RegistrationEnabled,
    bool RequireEmailVerification,
    int PasswordMinLength,
    int AccessTokenMinutes,
    int RefreshTokenDays,
    int MaxFailedAttempts,
    int LockoutMinutes,
    string? VerifyUrlTemplate,
    string? ResetUrlTemplate,
    string[]? AllowedEmailDomains);

public static class AuthDtoMappers
{
    public static AuthUserDto ToAuthUserDto(this AppUser u) => new(
        u.Id, u.Email, u.DisplayName, u.AvatarUrl,
        u.Metadata is null ? null : JsonDocument.Parse(u.Metadata).RootElement.Clone(),
        u.EmailVerifiedAt is not null, u.LastLoginAt, u.CreatedAt);

    /// <param name="persisted">False when the settings row has not been created yet (defaults).</param>
    public static AuthSettingsDto ToDto(this AuthSettings s, bool persisted = true) => new(
        s.RegistrationEnabled, s.RequireEmailVerification, s.PasswordMinLength, s.AccessTokenMinutes,
        s.RefreshTokenDays, s.MaxFailedAttempts, s.LockoutMinutes, s.VerifyUrlTemplate, s.ResetUrlTemplate,
        s.AllowedEmailDomains ?? [], persisted ? s.UpdatedAt : null);
}
