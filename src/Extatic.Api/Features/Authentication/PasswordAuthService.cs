using System.Text;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tokens = Extatic.Api.Common.Tokens;

namespace Extatic.Api.Features.Authentication;

/// <summary>Thrown when the password is right but the email must be verified first (403, code email_not_verified).</summary>
public class EmailNotVerifiedException() : Exception("Email address not verified")
{
    public const string Code = "email_not_verified";
}

/// <summary>
/// Email/password authentication for AppUsers (provider "password", provider_user_id = lowercase email).
/// Every query is scoped by AppId; tokens are stored only as SHA-256 hashes and are single-use.
/// </summary>
public class PasswordAuthService(
    AppDbContext db,
    AuthSettingsService settingsService,
    IEmailQueue emailQueue,
    WebhookDispatcher webhooks,
    AppUserTokenService accessTokens,
    TimeProvider time)
{
    public const string Provider = "password";
    public const int MaxMetadataBytes = 8 * 1024;
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    public const string GenericAccepted = "If the request can be processed, an email is on its way.";
    private const string InvalidCredentials = "Invalid email or password";
    private const string InvalidToken = "Invalid or expired token";

    private static readonly PasswordHasher<AppUserCredential> Hasher = new();
    // Verified against for unknown users so every login performs one hash verification.
    private static readonly Lazy<string> DummyHash =
        new(() => Hasher.HashPassword(new AppUserCredential(), Tokens.NewUrlToken()));

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ register / verify

    public async Task RegisterAsync(App app, RegisterRequest req, string ipHash)
    {
        var (settings, _) = await settingsService.GetAsync(app.Id);
        if (!settings.RegistrationEnabled)
            throw new ForbiddenException("Registration is disabled for this app");

        var email = NormaliseEmail(req.Email);
        if (settings.AllowedEmailDomains is { Length: > 0 } domains
            && !domains.Contains(email[(email.LastIndexOf('@') + 1)..], StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("Email domain not allowed",
                [new ValidationError("email", "Registration is not open for this email domain")]);
        PasswordPolicy.EnsureValid(req.Password, email, settings.PasswordMinLength);
        var displayName = NormaliseDisplayName(req.DisplayName);

        // Hash before the existence check so both branches cost the same.
        var passwordHash = Hasher.HashPassword(new AppUserCredential(), req.Password!);

        var exists = await db.AppUsers.AnyAsync(u =>
            u.AppId == app.Id && u.Provider == Provider && u.ProviderUserId == email);
        if (exists)
        {
            await NotifyExistingAccountAsync(app, email);
            return;
        }

        var now = Now;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Provider = Provider,
            ProviderUserId = email,
            Email = email,
            DisplayName = displayName
        };
        db.AppUsers.Add(user);
        db.AppUserCredentials.Add(new AppUserCredential
        {
            AppId = app.Id,
            AppUser = user,
            PasswordHash = passwordHash,
            PasswordChangedAt = now
        });
        if (settings.RequireEmailVerification)
        {
            var token = AddToken(app.Id, user.Id, AuthTokenPurpose.EmailVerification, VerificationLifetime, ipHash);
            emailQueue.Stage(db, app.Id, AuthEmails.Verification(app, email, token, settings.VerifyUrlTemplate));
        }
        await StageEventAsync(app.Id, "appuser.registered", user);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent registration for the same email won: behave exactly like "already exists".
            db.ChangeTracker.Clear();
            await NotifyExistingAccountAsync(app, email);
        }
    }

    private async Task NotifyExistingAccountAsync(App app, string email)
    {
        emailQueue.Stage(db, app.Id, AuthEmails.ExistingAccount(app, email));
        await db.SaveChangesAsync();
    }

    public async Task<AppUser> VerifyEmailAsync(App app, string? rawToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var token = await ConsumeTokenAsync(app.Id, rawToken, AuthTokenPurpose.EmailVerification);
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == token.AppUserId && u.AppId == app.Id)
            ?? throw new BadRequestException(InvalidToken);
        if (user.EmailVerifiedAt is null)
        {
            user.EmailVerifiedAt = Now;
            await StageEventAsync(app.Id, "appuser.verified", user);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return user;
    }

    public async Task ResendVerificationAsync(App app, string? rawEmail, string ipHash)
    {
        var email = NormaliseEmail(rawEmail);
        var (settings, _) = await settingsService.GetAsync(app.Id);
        var user = await FindPasswordUserAsync(app.Id, email);
        if (user is null || user.EmailVerifiedAt is not null || user.DisabledAt is not null) return;

        await RevokeOutstandingAsync(app.Id, user.Id, AuthTokenPurpose.EmailVerification);
        var token = AddToken(app.Id, user.Id, AuthTokenPurpose.EmailVerification, VerificationLifetime, ipHash);
        emailQueue.Stage(db, app.Id, AuthEmails.Verification(app, email, token, settings.VerifyUrlTemplate));
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ login / refresh / logout

    public async Task<AuthTokensResponse> LoginAsync(App app, LoginRequest req, string ipHash)
    {
        var (settings, _) = await settingsService.GetAsync(app.Id);
        var email = TryNormaliseEmail(req.Email);
        var password = req.Password ?? string.Empty;

        AppUser? user = null;
        AppUserCredential? credential = null;
        if (email is not null && password.Length <= PasswordPolicy.MaxLength)
        {
            user = await FindPasswordUserAsync(app.Id, email);
            if (user is not null)
                credential = await db.AppUserCredentials.FirstOrDefaultAsync(c =>
                    c.AppId == app.Id && c.AppUserId == user.Id);
        }

        if (user is null || credential is null)
        {
            Hasher.VerifyHashedPassword(new AppUserCredential(), DummyHash.Value, password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var result = Hasher.VerifyHashedPassword(credential, credential.PasswordHash, password);
        EnsureNotLocked(credential);
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(credential, settings);
            throw new UnauthorizedException(InvalidCredentials);
        }
        if (user.DisabledAt is not null)
            throw new UnauthorizedException(InvalidCredentials);
        if (settings.RequireEmailVerification && user.EmailVerifiedAt is null)
            throw new EmailNotVerifiedException();

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            credential.PasswordHash = Hasher.HashPassword(credential, password);
        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        user.LastLoginAt = Now;
        var refresh = AddToken(app.Id, user.Id, AuthTokenPurpose.Refresh,
            TimeSpan.FromDays(settings.RefreshTokenDays), ipHash);
        await StageEventAsync(app.Id, "appuser.login", user);
        await db.SaveChangesAsync();
        return IssueTokens(user, settings, refresh);
    }

    public async Task<AuthTokensResponse> RefreshAsync(App app, string? rawToken, string ipHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new UnauthorizedException(InvalidToken);
        var hash = Tokens.Hash(rawToken);
        var token = await db.AppUserAuthTokens.AsNoTracking().FirstOrDefaultAsync(t =>
            t.AppId == app.Id && t.TokenHash == hash && t.Purpose == AuthTokenPurpose.Refresh);
        var now = Now;
        if (token is null || token.RevokedAt is not null) throw new UnauthorizedException(InvalidToken);
        if (token.UsedAt is not null)
        {
            // Reuse of a rotated token: assume theft and kill the whole family.
            await RevokeFamilyAsync(app.Id, token.FamilyId);
            throw new UnauthorizedException(InvalidToken);
        }
        if (token.ExpiresAt <= now) throw new UnauthorizedException(InvalidToken);

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == token.AppUserId && u.AppId == app.Id);
        if (user is null || user.DisabledAt is not null)
        {
            await RevokeFamilyAsync(app.Id, token.FamilyId);
            throw new UnauthorizedException(InvalidToken);
        }
        var (settings, _) = await settingsService.GetAsync(app.Id);

        await using var tx = await db.Database.BeginTransactionAsync();
        var raw = Tokens.NewUrlToken();
        var replacement = NewToken(app.Id, user.Id, AuthTokenPurpose.Refresh, raw,
            TimeSpan.FromDays(settings.RefreshTokenDays), ipHash, token.FamilyId);
        // Atomic claim: only one concurrent caller can rotate a given token.
        var claimed = await db.AppUserAuthTokens
            .Where(t => t.Id == token.Id && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.UsedAt, now)
                .SetProperty(t => t.ReplacedByTokenId, replacement.Id));
        if (claimed == 0)
        {
            await tx.RollbackAsync();
            await RevokeFamilyAsync(app.Id, token.FamilyId);
            throw new UnauthorizedException(InvalidToken);
        }
        db.AppUserAuthTokens.Add(replacement);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return IssueTokens(user, settings, raw);
    }

    /// <summary>Revokes the refresh token's family. Unknown tokens are ignored (no information leak).</summary>
    public async Task LogoutAsync(App app, string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Tokens.Hash(rawToken);
        var familyId = await db.AppUserAuthTokens
            .Where(t => t.AppId == app.Id && t.TokenHash == hash && t.Purpose == AuthTokenPurpose.Refresh)
            .Select(t => (Guid?)t.FamilyId)
            .FirstOrDefaultAsync();
        if (familyId is not null) await RevokeFamilyAsync(app.Id, familyId.Value);
    }

    public Task<int> RevokeAllSessionsAsync(Guid appId, Guid appUserId)
        => db.AppUserAuthTokens
            .Where(t => t.AppId == appId && t.AppUserId == appUserId
                && t.Purpose == AuthTokenPurpose.Refresh && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now));

    // ------------------------------------------------------------------ password reset

    public async Task ForgotPasswordAsync(App app, string? rawEmail, string ipHash)
    {
        var email = NormaliseEmail(rawEmail);
        var user = await FindPasswordUserAsync(app.Id, email);
        if (user is null || user.DisabledAt is not null) return;
        if (!await db.AppUserCredentials.AnyAsync(c => c.AppId == app.Id && c.AppUserId == user.Id)) return;
        await StagePasswordResetAsync(app, user, ipHash);
        await db.SaveChangesAsync();
    }

    public async Task SendPasswordResetAsync(App app, Guid appUserId, string ipHash)
    {
        var user = await GetUserAsync(app.Id, appUserId);
        if (user.Email is null
            || !await db.AppUserCredentials.AnyAsync(c => c.AppId == app.Id && c.AppUserId == user.Id))
            throw new BadRequestException("This user does not sign in with a password");
        await StagePasswordResetAsync(app, user, ipHash);
        await db.SaveChangesAsync();
    }

    private async Task StagePasswordResetAsync(App app, AppUser user, string ipHash)
    {
        var (settings, _) = await settingsService.GetAsync(app.Id);
        await RevokeOutstandingAsync(app.Id, user.Id, AuthTokenPurpose.PasswordReset);
        var token = AddToken(app.Id, user.Id, AuthTokenPurpose.PasswordReset, ResetLifetime, ipHash);
        emailQueue.Stage(db, app.Id, AuthEmails.PasswordReset(app, user.Email!, token, settings.ResetUrlTemplate));
    }

    public async Task ResetPasswordAsync(App app, ResetPasswordRequest req)
    {
        // Validate the token and the new password before consuming the token.
        var token = await FindValidTokenAsync(app.Id, req.Token, AuthTokenPurpose.PasswordReset);
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == token.AppUserId && u.AppId == app.Id)
            ?? throw new BadRequestException(InvalidToken);
        var credential = await db.AppUserCredentials.FirstOrDefaultAsync(c =>
            c.AppId == app.Id && c.AppUserId == user.Id) ?? throw new BadRequestException(InvalidToken);
        var (settings, _) = await settingsService.GetAsync(app.Id);
        PasswordPolicy.EnsureValid(req.Password, user.Email, settings.PasswordMinLength);

        await using var tx = await db.Database.BeginTransactionAsync();
        await ConsumeTokenAsync(app.Id, req.Token, AuthTokenPurpose.PasswordReset);
        var now = Now;
        credential.PasswordHash = Hasher.HashPassword(credential, req.Password!);
        credential.PasswordChangedAt = now;
        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        if (user.EmailVerifiedAt is null)
        {
            // Following an emailed link proves ownership of the address.
            user.EmailVerifiedAt = now;
            await StageEventAsync(app.Id, "appuser.verified", user);
        }
        await RevokeAllSessionsAsync(app.Id, user.Id);
        await RevokeOutstandingAsync(app.Id, user.Id, AuthTokenPurpose.PasswordReset);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // ------------------------------------------------------------------ signed-in user

    /// <summary>The signed-in user. Disabled or deleted users get 401 even with a still-valid access token.</summary>
    public async Task<AppUser> GetActiveUserAsync(Guid appId, Guid appUserId)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == appUserId && u.AppId == appId);
        if (user is null || user.DisabledAt is not null) throw new UnauthorizedException("Sign-in required");
        return user;
    }

    public async Task<AppUser> UpdateProfileAsync(Guid appId, Guid appUserId, UpdateProfileRequest req)
    {
        var user = await GetActiveUserAsync(appId, appUserId);
        var errors = new List<ValidationError>();
        var displayName = req.DisplayName?.Trim();
        if (displayName is { Length: > 256 })
            errors.Add(new ValidationError("display_name", "Must be at most 256 characters"));
        var avatar = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim();
        if (avatar is not null && (avatar.Length > 2048
            || !Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)))
            errors.Add(new ValidationError("avatar_url", "Must be an absolute https URL (max 2048 characters)"));
        string? metadata = null;
        if (req.Metadata is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } m)
        {
            if (m.ValueKind != JsonValueKind.Object)
                errors.Add(new ValidationError("metadata", "Must be a JSON object"));
            else
            {
                metadata = m.GetRawText();
                if (Encoding.UTF8.GetByteCount(metadata) > MaxMetadataBytes)
                    errors.Add(new ValidationError("metadata", $"Must be at most {MaxMetadataBytes} bytes"));
            }
        }
        if (errors.Count > 0) throw new ValidationException("Invalid profile", errors);

        user.DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        user.AvatarUrl = avatar;
        user.Metadata = metadata;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<AuthTokensResponse> ChangePasswordAsync(App app, Guid appUserId, ChangePasswordRequest req,
        string ipHash)
    {
        var user = await GetActiveUserAsync(app.Id, appUserId);
        var credential = await GetCredentialAsync(app.Id, user.Id);
        var (settings, _) = await settingsService.GetAsync(app.Id);
        PasswordPolicy.EnsureValid(req.NewPassword, user.Email, settings.PasswordMinLength, "new_password");
        await VerifyCurrentPasswordAsync(credential, settings, req.CurrentPassword, "current_password");

        credential.PasswordHash = Hasher.HashPassword(credential, req.NewPassword!);
        credential.PasswordChangedAt = Now;
        await RevokeAllSessionsAsync(app.Id, user.Id);
        // The caller keeps a fresh session; every other session is gone.
        var refresh = AddToken(app.Id, user.Id, AuthTokenPurpose.Refresh,
            TimeSpan.FromDays(settings.RefreshTokenDays), ipHash);
        await db.SaveChangesAsync();
        return IssueTokens(user, settings, refresh);
    }

    public async Task DeleteAccountAsync(App app, Guid appUserId, string? password)
    {
        var user = await GetActiveUserAsync(app.Id, appUserId);
        var credential = await GetCredentialAsync(app.Id, user.Id);
        var (settings, _) = await settingsService.GetAsync(app.Id);
        await VerifyCurrentPasswordAsync(credential, settings, password, "password");

        await StageEventAsync(app.Id, "appuser.deleted", user);
        db.AppUsers.Remove(user);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ platform admin

    public async Task<AppUser> GetUserAsync(Guid appId, Guid appUserId)
        => await db.AppUsers.FirstOrDefaultAsync(u => u.Id == appUserId && u.AppId == appId)
            ?? throw new NotFoundException("User not found");

    public async Task<AppUser> SetDisabledAsync(Guid appId, Guid appUserId, bool disabled)
    {
        var user = await GetUserAsync(appId, appUserId);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (disabled)
        {
            user.DisabledAt ??= Now;
            await RevokeAllSessionsAsync(appId, user.Id);
        }
        else
        {
            user.DisabledAt = null;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return user;
    }

    // ------------------------------------------------------------------ helpers

    private AuthTokensResponse IssueTokens(AppUser user, AuthSettings settings, string refreshToken)
    {
        var lifetime = TimeSpan.FromMinutes(settings.AccessTokenMinutes);
        return new AuthTokensResponse(accessTokens.CreateAccessToken(user, lifetime), "Bearer",
            (int)lifetime.TotalSeconds, refreshToken, user.ToAuthUserDto());
    }

    private async Task<AppUserCredential> GetCredentialAsync(Guid appId, Guid appUserId)
        => await db.AppUserCredentials.FirstOrDefaultAsync(c => c.AppId == appId && c.AppUserId == appUserId)
            ?? throw new BadRequestException("This account does not sign in with a password");

    private async Task VerifyCurrentPasswordAsync(AppUserCredential credential, AuthSettings settings,
        string? password, string pointer)
    {
        EnsureNotLocked(credential);
        var result = string.IsNullOrEmpty(password) || password.Length > PasswordPolicy.MaxLength
            ? PasswordVerificationResult.Failed
            : Hasher.VerifyHashedPassword(credential, credential.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(credential, settings);
            throw new ValidationException("Incorrect password",
                [new ValidationError(pointer, "Password is incorrect")]);
        }
        credential.FailedAttempts = 0;
    }

    private void EnsureNotLocked(AppUserCredential credential)
    {
        var now = Now;
        if (credential.LockedUntil is { } until && until > now)
            throw new TooManyRequestsException("Too many failed sign-in attempts. Try again later.", until - now);
    }

    /// <summary>Atomically counts a failure; reaching the limit locks the account and resets the counter.</summary>
    private async Task RegisterFailureAsync(AppUserCredential credential, AuthSettings settings)
    {
        var max = settings.MaxFailedAttempts;
        DateTime? lockUntil = Now.AddMinutes(settings.LockoutMinutes);
        await db.AppUserCredentials
            .Where(c => c.Id == credential.Id && c.AppId == credential.AppId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LockedUntil, c => c.FailedAttempts + 1 >= max ? lockUntil : c.LockedUntil)
                .SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1 >= max ? 0 : c.FailedAttempts + 1));
    }

    private Task<AppUser?> FindPasswordUserAsync(Guid appId, string email)
        => db.AppUsers.FirstOrDefaultAsync(u => u.AppId == appId && u.Provider == Provider && u.ProviderUserId == email);

    private string AddToken(Guid appId, Guid appUserId, string purpose, TimeSpan lifetime, string ipHash)
    {
        var raw = Tokens.NewUrlToken();
        db.AppUserAuthTokens.Add(NewToken(appId, appUserId, purpose, raw, lifetime, ipHash, null));
        return raw;
    }

    private AppUserAuthToken NewToken(Guid appId, Guid appUserId, string purpose, string raw, TimeSpan lifetime,
        string ipHash, Guid? familyId) => new()
    {
        Id = Guid.NewGuid(),
        AppId = appId,
        AppUserId = appUserId,
        Purpose = purpose,
        TokenHash = Tokens.Hash(raw),
        ExpiresAt = Now.Add(lifetime),
        FamilyId = familyId ?? Guid.NewGuid(),
        IpHash = ipHash
    };

    private async Task<AppUserAuthToken> FindValidTokenAsync(Guid appId, string? raw, string purpose)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256) throw new BadRequestException(InvalidToken);
        var hash = Tokens.Hash(raw);
        var token = await db.AppUserAuthTokens.AsNoTracking().FirstOrDefaultAsync(t =>
            t.AppId == appId && t.TokenHash == hash && t.Purpose == purpose);
        if (token is null || token.UsedAt is not null || token.RevokedAt is not null || token.ExpiresAt <= Now)
            throw new BadRequestException(InvalidToken);
        return token;
    }

    /// <summary>Validates and atomically marks the token used; a concurrent second use fails.</summary>
    private async Task<AppUserAuthToken> ConsumeTokenAsync(Guid appId, string? raw, string purpose)
    {
        var token = await FindValidTokenAsync(appId, raw, purpose);
        var now = Now;
        var claimed = await db.AppUserAuthTokens
            .Where(t => t.Id == token.Id && t.AppId == appId && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
        if (claimed == 0) throw new BadRequestException(InvalidToken);
        return token;
    }

    private Task<int> RevokeOutstandingAsync(Guid appId, Guid appUserId, string purpose)
        => db.AppUserAuthTokens
            .Where(t => t.AppId == appId && t.AppUserId == appUserId && t.Purpose == purpose
                && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now));

    private Task<int> RevokeFamilyAsync(Guid appId, Guid familyId)
        => db.AppUserAuthTokens
            .Where(t => t.AppId == appId && t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now));

    private Task StageEventAsync(Guid appId, string eventType, AppUser user)
        => webhooks.StageAsync(db, appId, eventType, JsonSerializer.SerializeToElement(new
        {
            app_user_id = user.Id,
            email = user.Email,
            display_name = user.DisplayName,
            provider = user.Provider,
            email_verified = user.EmailVerifiedAt is not null
        }));

    private static string NormaliseEmail(string? raw)
        => TryNormaliseEmail(raw) ?? throw new ValidationException("Invalid email",
            [new ValidationError("email", "Must be a valid email address")]);

    public static string? TryNormaliseEmail(string? raw)
    {
        var email = raw?.Trim().ToLowerInvariant();
        return email is { Length: <= 256 } && EmailQueue.IsValidAddress(email) ? email : null;
    }

    private static string? NormaliseDisplayName(string? raw)
    {
        var name = raw?.Trim();
        if (name is { Length: > 256 })
            throw new ValidationException("Invalid display name",
                [new ValidationError("display_name", "Must be at most 256 characters")]);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
