using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Authentication;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Authentication.AuthTestHarness;

namespace Extatic.Api.Tests.Features.Authentication;

public class PasswordAuthFlowTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly AuthTestHarness h = new(fixture);

    private async Task SubscribeAllAsync(App app)
    {
        await using var db = fixture.CreateContext();
        db.Webhooks.Add(new Webhook
        {
            AppId = app.Id, Url = "https://hooks.example.com/x", Secret = "s",
            Events = ["appuser.registered", "appuser.verified", "appuser.login", "appuser.deleted"]
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> EventsAsync(App app)
    {
        await using var db = fixture.CreateContext();
        return await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == app.Id)
            .OrderBy(l => l.CreatedAt).Select(l => l.EventType).ToListAsync();
    }

    [Fact]
    public async Task FullFlow_Register_Verify_Login_Refresh_Logout()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SubscribeAllAsync(app);
        await h.SaveSettingsAsync(app, s => s with { VerifyUrlTemplate = "https://site.example.com/verify?token={token}" });
        const string email = "Alice@Example.com";

        var registered = await h.Client(app, c => c.Register(new RegisterRequest(email, Password, "Alice")));
        Assert.Equal(202, Status(registered));

        // Not verified yet → 403 with machine-readable code.
        var blocked = await h.Client(app, c => c.Login(new LoginRequest(email, Password)));
        Assert.Equal(403, Status(blocked));
        var problem = Assert.IsType<ProblemDetails>(Value(blocked));
        Assert.Equal(EmailNotVerifiedException.Code, problem.Extensions["code"]);

        var verifyMail = (await h.EmailsAsync(app, "alice@example.com", AuthEmails.CategoryVerification)).Single();
        Assert.Contains("https://site.example.com/verify?token=", verifyMail.TextBody);
        var token = await h.LatestTokenAsync(app, "alice@example.com", AuthEmails.CategoryVerification);
        Assert.Equal(200, Status(await h.Client(app, c => c.VerifyEmail(new TokenRequest(token)))));

        var login = await h.LoginAsync(app, "alice@example.com");
        Assert.Equal(3600, login.ExpiresIn);
        Assert.True(login.User.EmailVerified);
        Assert.Equal("alice@example.com", login.User.Email);
        Assert.NotNull(login.User.LastLoginAt);

        var me = await h.Client(app, c => c.GetMe(), appUserId: login.User.Id);
        Assert.Equal("Alice", ((AuthUserDto)Value(me)!).DisplayName);

        var refreshed = await h.Run(s => s.RefreshAsync(app, login.RefreshToken, "ip"));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        Assert.Equal(204, Status(await h.Client(app, c => c.Logout(new RefreshRequest(refreshed.RefreshToken)))));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, refreshed.RefreshToken, "ip")));

        Assert.Equal(["appuser.registered", "appuser.verified", "appuser.login"], await EventsAsync(app));

        // Nothing secret stored in clear.
        await using var db = fixture.CreateContext();
        Assert.False(await db.AppUserAuthTokens.AnyAsync(t => t.TokenHash == token || t.TokenHash == login.RefreshToken));
        var credential = await db.AppUserCredentials.SingleAsync(c => c.AppUserId == login.User.Id);
        Assert.DoesNotContain(Password, credential.PasswordHash);
    }

    [Fact]
    public async Task AccessToken_SatisfiesAuthenticatedAppUserPolicy_OnlyForItsApp()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, other, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "pipe@example.com");
        var login = await h.LoginAsync(app, "pipe@example.com");
        using var pipeline = new TestAuthPipeline(fixture);

        async Task<bool> Authorize(App target)
        {
            var context = pipeline.CreateContext($"/api/{target.PublicId}/auth/me",
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {login.AccessToken}" });
            return (await pipeline.AuthorizeAsync(context, PolicyNames.AuthenticatedAppUser)).Succeeded;
        }

        Assert.True(await Authorize(app));
        Assert.False(await Authorize(other));
    }

    [Fact]
    public async Task EnumerationSafety_IdenticalResponses_ForExistingAndUnknownEmails()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "known@example.com");

        async Task<string> Body<T>(Func<ClientPasswordAuthController, Task<ActionResult<T>>> call)
        {
            var result = await h.Client(app, call);
            return $"{Status(result)}:{System.Text.Json.JsonSerializer.Serialize(Value(result))}";
        }

        Assert.Equal(await Body(c => c.Register(new RegisterRequest("known@example.com", Password, null))),
            await Body(c => c.Register(new RegisterRequest("new@example.com", Password, null))));
        Assert.Equal(await Body(c => c.ForgotPassword(new EmailRequest("known@example.com"))),
            await Body(c => c.ForgotPassword(new EmailRequest("nobody@example.com"))));
        Assert.Equal(await Body(c => c.ResendVerification(new EmailRequest("known@example.com"))),
            await Body(c => c.ResendVerification(new EmailRequest("nobody@example.com"))));

        // The owner of the existing account is told instead, and nothing was overwritten.
        Assert.Single(await h.EmailsAsync(app, "known@example.com", AuthEmails.CategoryExistingAccount));
        Assert.Single(await h.EmailsAsync(app, "known@example.com", AuthEmails.CategoryReset));
        Assert.Empty(await h.EmailsAsync(app, "nobody@example.com"));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.AppUsers.CountAsync(u => u.AppId == app.Id && u.Email == "known@example.com"));
    }

    [Fact]
    public async Task Login_UnknownUser_WrongPassword_AreGeneric401()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "x@example.com");
        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "y@example.com"));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "x@example.com", "wrong-password-1"));
        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task Lockout_AfterMaxFailures_ThenUnlocksAfterWindow()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { MaxFailedAttempts = 3, LockoutMinutes = 10 });
        await h.RegisterVerifiedAsync(app, "lock@example.com");

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "lock@example.com", "bad-password-x"));

        // Locked: even the right password gets 429 with Retry-After.
        var locked = await Assert.ThrowsAsync<TooManyRequestsException>(() => h.LoginAsync(app, "lock@example.com"));
        Assert.NotNull(locked.RetryAfter);
        Assert.InRange(locked.RetryAfter!.Value.TotalMinutes, 9, 10);

        h.Time.Advance(TimeSpan.FromMinutes(11));
        var ok = await h.LoginAsync(app, "lock@example.com");
        Assert.NotNull(ok.AccessToken);
        await using var db = fixture.CreateContext();
        var credential = await db.AppUserCredentials.SingleAsync(c => c.AppUserId == ok.User.Id);
        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockedUntil);
    }

    [Fact]
    public async Task Lockout_ParallelFailures_AreAllCounted()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { MaxFailedAttempts = 5 });
        await h.RegisterVerifiedAsync(app, "par@example.com");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "par@example.com", "bad-password-x"))));
        await Assert.ThrowsAsync<TooManyRequestsException>(() => h.LoginAsync(app, "par@example.com"));
    }

    [Fact]
    public async Task Refresh_RotatesOnce_ReuseRevokesWholeFamily()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "rot@example.com");
        var first = await h.LoginAsync(app, "rot@example.com");
        var other = await h.LoginAsync(app, "rot@example.com"); // a second, independent session

        var second = await h.Run(s => s.RefreshAsync(app, first.RefreshToken, "ip"));
        // Replay of the rotated token: rejected and the family (incl. `second`) is revoked.
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, first.RefreshToken, "ip")));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, second.RefreshToken, "ip")));
        // Other sessions are unaffected.
        Assert.NotNull((await h.Run(s => s.RefreshAsync(app, other.RefreshToken, "ip"))).AccessToken);
    }

    [Fact]
    public async Task Refresh_ParallelUseOfOneToken_OnlyOneSucceeds()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "race@example.com");
        var login = await h.LoginAsync(app, "race@example.com");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await h.Run(s => s.RefreshAsync(app, login.RefreshToken, "ip")); return true; }
            catch (UnauthorizedException) { return false; }
        }));
        Assert.True(results.Count(r => r) <= 1);
        await using var db = fixture.CreateContext();
        Assert.True(await db.AppUserAuthTokens.CountAsync(t => t.AppUserId == login.User.Id && t.Purpose == AuthTokenPurpose.Refresh && t.UsedAt != null) <= 1);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Fails()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { RefreshTokenDays = 1 });
        await h.RegisterVerifiedAsync(app, "exp@example.com");
        var login = await h.LoginAsync(app, "exp@example.com");
        h.Time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, login.RefreshToken, "ip")));
    }

    [Fact]
    public async Task ResetFlow_RevokesSessions_ClearsLockout_TokenSingleUseAndExpires()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { ResetUrlTemplate = "http://localhost:4000/reset#{token}", MaxFailedAttempts = 2 });
        await h.RegisterVerifiedAsync(app, "reset@example.com");
        var session = await h.LoginAsync(app, "reset@example.com");
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "reset@example.com", "bad-password-x"));

        Assert.Equal(202, Status(await h.Client(app, c => c.ForgotPassword(new EmailRequest("reset@example.com")))));
        var mail = (await h.EmailsAsync(app, "reset@example.com", AuthEmails.CategoryReset)).Single();
        Assert.Contains("http://localhost:4000/reset#", mail.TextBody);
        var token = mail.TextBody.Split("reset#")[1].Split('\n')[0].Trim();

        // Weak password is rejected without consuming the token.
        await Assert.ThrowsAsync<ValidationException>(() => h.Run(s => s.ResetPasswordAsync(app, new ResetPasswordRequest(token, "password123"))));
        await h.Run(s => s.ResetPasswordAsync(app, new ResetPasswordRequest(token, "a-brand-new-passphrase")));

        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.ResetPasswordAsync(app, new ResetPasswordRequest(token, "another-new-passphrase"))));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, session.RefreshToken, "ip")));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "reset@example.com"));
        Assert.NotNull((await h.LoginAsync(app, "reset@example.com", "a-brand-new-passphrase")).AccessToken);

        // A new reset token expires after one hour.
        await h.Run(s => s.ForgotPasswordAsync(app, "reset@example.com", "ip"));
        var late = (await h.EmailsAsync(app, "reset@example.com", AuthEmails.CategoryReset)).Last().TextBody
            .Split("reset#")[1].Split('\n')[0].Trim();
        h.Time.Advance(TimeSpan.FromMinutes(61));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.ResetPasswordAsync(app, new ResetPasswordRequest(late, "yet-another-passphrase"))));
    }

    [Fact]
    public async Task ForgotPassword_SecondRequest_InvalidatesFirstToken()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "twice@example.com");
        await h.Run(s => s.ForgotPasswordAsync(app, "twice@example.com", "ip"));
        var first = await h.LatestTokenAsync(app, "twice@example.com", AuthEmails.CategoryReset);
        await h.Run(s => s.ForgotPasswordAsync(app, "twice@example.com", "ip"));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.ResetPasswordAsync(app, new ResetPasswordRequest(first, "fresh-passphrase-1"))));
    }

    [Fact]
    public async Task VerificationToken_SingleUse_ExpiresAndResendReplacesIt()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterAsync(app, "ver@example.com");
        var first = await h.LatestTokenAsync(app, "ver@example.com", AuthEmails.CategoryVerification);
        Assert.DoesNotContain("token=", (await h.EmailsAsync(app, "ver@example.com")).Single().TextBody); // no template → code

        h.Time.Advance(TimeSpan.FromHours(25));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.VerifyEmailAsync(app, first)));

        await h.Run(s => s.ResendVerificationAsync(app, "ver@example.com", "ip"));
        var second = await h.LatestTokenAsync(app, "ver@example.com", AuthEmails.CategoryVerification);
        await h.Run(s => s.VerifyEmailAsync(app, second));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.VerifyEmailAsync(app, second)));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.VerifyEmailAsync(app, "garbage")));

        // Verified users get no more verification emails.
        await h.Run(s => s.ResendVerificationAsync(app, "ver@example.com", "ip"));
        Assert.Equal(2, (await h.EmailsAsync(app, "ver@example.com", AuthEmails.CategoryVerification)).Count);
    }

    [Fact]
    public async Task VerificationToken_ParallelUse_OnlyOneSucceeds()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterAsync(app, "pv@example.com");
        var token = await h.LatestTokenAsync(app, "pv@example.com", AuthEmails.CategoryVerification);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { await h.Run(s => s.VerifyEmailAsync(app, token)); return true; }
            catch (BadRequestException) { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task RegistrationWithoutVerification_LogsInImmediately_NoVerificationEmail()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { RequireEmailVerification = false, AccessTokenMinutes = 15 });
        await h.RegisterAsync(app, "now@example.com");
        Assert.Empty(await h.EmailsAsync(app, "now@example.com"));
        var login = await h.LoginAsync(app, "now@example.com");
        Assert.Equal(900, login.ExpiresIn);
        Assert.False(login.User.EmailVerified);
    }

    [Theory]
    [InlineData("short", "at least 10")]
    [InlineData("password123", "too common")]
    [InlineData("Qwertyuiop", "too common")]
    [InlineData("policy@example.com", "email")]
    public async Task PasswordPolicy_IsEnforcedOnRegister(string password, string expected)
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.RegisterAsync(app, "policy@example.com", password));
        var error = Assert.Single(ex.Errors);
        Assert.Equal("password", error.Pointer);
        Assert.Contains(expected, error.Message);
        Assert.Empty(PasswordPolicy.Validate("a-perfectly-fine-pass", "policy@example.com", 10));
        Assert.NotEmpty(PasswordPolicy.Validate("12345678", null, 4)); // absolute floor 8 + common list
    }

    [Fact]
    public async Task Register_InvalidEmail_DisabledRegistration_DomainAllowlist()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await Assert.ThrowsAsync<ValidationException>(() => h.RegisterAsync(app, "not-an-email"));

        await h.SaveSettingsAsync(app, s => s with { AllowedEmailDomains = ["@Corp.Example.com"] });
        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.RegisterAsync(app, "a@gmail.com"));
        Assert.Equal("email", ex.Errors.Single().Pointer);
        await h.RegisterAsync(app, "a@corp.example.com");

        await h.SaveSettingsAsync(app, s => s with { RegistrationEnabled = false });
        await Assert.ThrowsAsync<ForbiddenException>(() => h.RegisterAsync(app, "b@corp.example.com"));
    }

    [Fact]
    public async Task ParallelDuplicateRegistration_CreatesExactlyOneUser()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => h.RegisterAsync(app, "dup@example.com")));

        await using var db = fixture.CreateContext();
        var users = await db.AppUsers.Where(u => u.AppId == app.Id && u.ProviderUserId == "dup@example.com").ToListAsync();
        var user = Assert.Single(users);
        Assert.Equal(1, await db.AppUserCredentials.CountAsync(c => c.AppUserId == user.Id));
        Assert.Single(await h.EmailsAsync(app, "dup@example.com", AuthEmails.CategoryVerification));
        Assert.Equal(7, (await h.EmailsAsync(app, "dup@example.com", AuthEmails.CategoryExistingAccount)).Count);
    }

    [Fact]
    public async Task CrossTenant_SameEmailTwoApps_AreUnrelated_AndTokensDoNotCross()
    {
        var (_, a, _) = await fixture.SeedAppAsync();
        var (_, b, _) = await fixture.SeedAppAsync();
        var userA = await h.RegisterVerifiedAsync(a, "both@example.com");
        await h.RegisterAsync(b, "both@example.com", "a-different-passphrase");
        var verifyB = await h.LatestTokenAsync(b, "both@example.com", AuthEmails.CategoryVerification);

        // App A's credentials / tokens never work on App B.
        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => h.LoginAsync(b, "both@example.com", "a-different-passphrase"));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(b, "both@example.com"));
        var sessionA = await h.LoginAsync(a, "both@example.com");
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(b, sessionA.RefreshToken, "ip")));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Run(s => s.VerifyEmailAsync(a, verifyB)));
        await h.Run(s => s.LogoutAsync(b, sessionA.RefreshToken)); // no effect on A
        Assert.NotNull((await h.Run(s => s.RefreshAsync(a, sessionA.RefreshToken, "ip"))).AccessToken);

        // A token for App A presented to App B's /me is anonymous → 401.
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            h.Client(b, c => c.GetMe(), appUserId: userA.Id, tokenAppId: a.Id));
        // Even an id-forged token scoped to B cannot read A's user.
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Client(b, c => c.GetMe(), appUserId: userA.Id));

        await h.Run(s => s.VerifyEmailAsync(b, verifyB));
        var sessionB = await h.LoginAsync(b, "both@example.com", "a-different-passphrase");
        Assert.NotEqual(userA.Id, sessionB.User.Id);

        // Platform actions on another App's user are 404.
        await Assert.ThrowsAsync<NotFoundException>(() => h.Platform(b, Guid.NewGuid(), c => c.Disable(userA.Id)));
    }

    [Fact]
    public async Task DisabledUser_CannotLoginRefreshOrUseMe_UntilEnabled()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "dis@example.com");
        var session = await h.LoginAsync(app, "dis@example.com");

        var disabled = await h.Platform(app, owner.Id, c => c.Disable(session.User.Id));
        Assert.True(((Extatic.Api.Dtos.AppUserDto)Value(disabled)!).Disabled);

        await Assert.ThrowsAsync<UnauthorizedException>(() => h.LoginAsync(app, "dis@example.com"));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, session.RefreshToken, "ip")));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Client(app, c => c.GetMe(), appUserId: session.User.Id));

        await h.Platform(app, owner.Id, c => c.Enable(session.User.Id));
        // Sessions revoked by disabling stay revoked; a fresh login works.
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, session.RefreshToken, "ip")));
        Assert.NotNull((await h.LoginAsync(app, "dis@example.com")).AccessToken);
    }

    [Fact]
    public async Task Platform_RevokeSessions_And_SendPasswordReset()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "adm@example.com");
        var session = await h.LoginAsync(app, "adm@example.com");

        Assert.Equal(204, Status(await h.Platform(app, owner.Id, c => c.RevokeSessions(session.User.Id))));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, session.RefreshToken, "ip")));

        Assert.Equal(202, Status(await h.Platform(app, owner.Id, c => c.SendPasswordReset(session.User.Id))));
        Assert.Single(await h.EmailsAsync(app, "adm@example.com", AuthEmails.CategoryReset));

        var devUser = await fixture.SeedAppUserAsync(app.Id); // no password credential
        await Assert.ThrowsAsync<BadRequestException>(() => h.Platform(app, owner.Id, c => c.SendPasswordReset(devUser.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Platform(app, owner.Id, c => c.RevokeSessions(Guid.NewGuid())));
    }

    [Fact]
    public async Task Me_UpdateProfile_Validates()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var user = await h.RegisterVerifiedAsync(app, "me@example.com");
        var meta = System.Text.Json.JsonDocument.Parse("""{"theme":"dark"}""").RootElement;

        var updated = await h.Client(app, c => c.UpdateMe(new UpdateProfileRequest(" New Name ", "https://cdn.example.com/a.png", meta)), user.Id);
        var dto = (AuthUserDto)Value(updated)!;
        Assert.Equal("New Name", dto.DisplayName);
        Assert.Equal("dark", dto.Metadata!.Value.GetProperty("theme").GetString());

        async Task<string> Fails(UpdateProfileRequest req)
            => (await Assert.ThrowsAsync<ValidationException>(() => h.Client(app, c => c.UpdateMe(req), user.Id))).Errors.Single().Pointer;
        Assert.Equal("avatar_url", await Fails(new UpdateProfileRequest(null, "http://cdn.example.com/a.png", null)));
        Assert.Equal("avatar_url", await Fails(new UpdateProfileRequest(null, "javascript:alert(1)", null)));
        Assert.Equal("metadata", await Fails(new UpdateProfileRequest(null, null, System.Text.Json.JsonDocument.Parse("[1]").RootElement)));
        var big = System.Text.Json.JsonDocument.Parse($$"""{"x":"{{new string('a', 9000)}}"}""").RootElement;
        Assert.Equal("metadata", await Fails(new UpdateProfileRequest(null, null, big)));
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherSessions_AndReturnsFreshOne()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var user = await h.RegisterVerifiedAsync(app, "cp@example.com");
        var other = await h.LoginAsync(app, "cp@example.com");

        await Assert.ThrowsAsync<ValidationException>(() => h.Run(s =>
            s.ChangePasswordAsync(app, user.Id, new ChangePasswordRequest("wrong-password-x", "new-passphrase-ok"), "ip")));
        var fresh = await h.Run(s =>
            s.ChangePasswordAsync(app, user.Id, new ChangePasswordRequest(Password, "new-passphrase-ok"), "ip"));

        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, other.RefreshToken, "ip")));
        Assert.NotNull((await h.Run(s => s.RefreshAsync(app, fresh.RefreshToken, "ip"))).AccessToken);
        Assert.NotNull((await h.LoginAsync(app, "cp@example.com", "new-passphrase-ok")).AccessToken);
    }

    [Fact]
    public async Task DeleteMe_RequiresPassword_DeletesUserAndTokens()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SubscribeAllAsync(app);
        var user = await h.RegisterVerifiedAsync(app, "del@example.com");
        await h.LoginAsync(app, "del@example.com");

        await Assert.ThrowsAsync<ValidationException>(() => h.Client(app, c => c.DeleteMe(new DeleteAccountRequest("nope-nope-nope")), user.Id));
        Assert.Equal(204, Status(await h.Client(app, c => c.DeleteMe(new DeleteAccountRequest(Password)), user.Id)));

        await using var db = fixture.CreateContext();
        Assert.False(await db.AppUsers.AnyAsync(u => u.Id == user.Id));
        Assert.False(await db.AppUserAuthTokens.AnyAsync(t => t.AppUserId == user.Id));
        Assert.False(await db.AppUserCredentials.AnyAsync(c => c.AppUserId == user.Id));
        Assert.Contains("appuser.deleted", await EventsAsync(app));
    }

    [Fact]
    public async Task LogoutAll_RevokesEverySession()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "all@example.com");
        var s1 = await h.LoginAsync(app, "all@example.com");
        var s2 = await h.LoginAsync(app, "all@example.com");
        Assert.Equal(204, Status(await h.Client(app, c => c.LogoutAll(), s1.User.Id)));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, s1.RefreshToken, "ip")));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Run(s => s.RefreshAsync(app, s2.RefreshToken, "ip")));
    }

    [Fact]
    public async Task Cleanup_PurgesDeadTokens_KeepsLiveAndRotatedRefresh()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.RegisterVerifiedAsync(app, "gc@example.com");
        var login = await h.LoginAsync(app, "gc@example.com");
        await h.Run(s => s.RefreshAsync(app, login.RefreshToken, "ip"));

        await using var db = fixture.CreateContext();
        var now = h.Time.GetUtcNow().UtcDateTime;
        await AuthTokenCleanupService.PurgeAsync(db, now);
        // used verification token (just now) + used refresh + live refresh remain
        Assert.Equal(3, await db.AppUserAuthTokens.CountAsync(t => t.AppUserId == login.User.Id));

        await AuthTokenCleanupService.PurgeAsync(db, now.AddDays(8));
        // verification gone (used > 7 days ago); refresh tokens live for 30 days so both kept
        Assert.Equal(2, await db.AppUserAuthTokens.CountAsync(t => t.AppUserId == login.User.Id));
        await AuthTokenCleanupService.PurgeAsync(db, now.AddDays(38));
        Assert.Equal(0, await db.AppUserAuthTokens.CountAsync(t => t.AppUserId == login.User.Id));
    }

    [Fact]
    public void Module_DeclaresWebhookEvents()
    {
        var module = new AuthenticationModule();
        Assert.Equal("authentication", module.Key);
        Assert.Contains("appuser.registered", module.WebhookEvents);
        Assert.Contains("appuser.deleted", module.WebhookEvents);
    }
}
