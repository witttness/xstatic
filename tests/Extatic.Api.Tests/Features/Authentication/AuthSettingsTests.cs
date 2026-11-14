using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Authentication;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Authentication.AuthTestHarness;

namespace Extatic.Api.Tests.Features.Authentication;

public class AuthSettingsTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly AuthTestHarness h = new(fixture);

    private static UpdateAuthSettingsRequest Defaults => new(true, true, 10, 60, 30, 5, 15, null, null, null);

    [Fact]
    public async Task Get_ReturnsDefaults_WithoutCreatingRow_ThenPutPersists()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var dto = (AuthSettingsDto)Value(await h.Platform(app, owner.Id, c => c.GetSettings()))!;
        Assert.True(dto.RegistrationEnabled);
        Assert.True(dto.RequireEmailVerification);
        Assert.Equal(10, dto.PasswordMinLength);
        Assert.Null(dto.UpdatedAt);
        await using (var db = fixture.CreateContext())
            Assert.False(await db.AuthSettings.AnyAsync(s => s.AppId == app.Id));

        var saved = (AuthSettingsDto)Value(await h.Platform(app, owner.Id, c => c.UpdateSettings(Defaults with
        {
            PasswordMinLength = 12,
            AllowedEmailDomains = [" Example.COM ", "@example.com", "corp.example.org"],
            VerifyUrlTemplate = " https://site.example.com/verify?t={token} "
        })))!;
        Assert.Equal(12, saved.PasswordMinLength);
        Assert.Equal(["example.com", "corp.example.org"], saved.AllowedEmailDomains);
        Assert.Equal("https://site.example.com/verify?t={token}", saved.VerifyUrlTemplate);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task ParallelFirstSaves_CreateOneRow()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            h.SaveSettingsAsync(app, s => s with { LockoutMinutes = 10 + i })));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.AuthSettings.CountAsync(s => s.AppId == app.Id));
    }

    [Theory]
    [InlineData("http://site.example.com/v?t={token}", "https")]
    [InlineData("https://site.example.com/verify", "{token}")]
    [InlineData("/verify?t={token}", "absolute")]
    [InlineData("ftp://site.example.com/{token}", "https")]
    [InlineData("https://user:pw@site.example.com/{token}", "absolute")]
    [InlineData("javascript:alert('{token}')", "absolute")]
    public void UrlTemplate_Invalid(string template, string expected)
    {
        var errors = new List<ValidationError>();
        Assert.Null(AuthSettingsService.ValidateTemplate(new App(), template, "verify_url_template", errors));
        Assert.Contains(expected, Assert.Single(errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://site.example.com/verify?token={token}")]
    [InlineData("http://localhost:4200/verify#{token}")]
    [InlineData("http://127.0.0.1:8080/v/{token}")]
    public void UrlTemplate_Valid(string template)
    {
        var errors = new List<ValidationError>();
        Assert.Equal(template, AuthSettingsService.ValidateTemplate(new App(), template, "p", errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void UrlTemplate_MustMatchAllowedOrigins_WhenConfigured()
    {
        var app = new App { AllowedOrigins = ["https://mysite.example.com"] };
        var errors = new List<ValidationError>();
        Assert.NotNull(AuthSettingsService.ValidateTemplate(app, "https://mysite.example.com/v?t={token}", "p", errors));
        Assert.NotNull(AuthSettingsService.ValidateTemplate(app, "http://localhost:3000/v?t={token}", "p", errors));
        Assert.Empty(errors);
        Assert.Null(AuthSettingsService.ValidateTemplate(app, "https://evil.example.net/v?t={token}", "p", errors));
        Assert.Contains("allowed origins", Assert.Single(errors).Message);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeValuesAndBadDomains()
    {
        var ex = Assert.Throws<ValidationException>(() => AuthSettingsService.Validate(new App(), Defaults with
        {
            PasswordMinLength = 6,
            AccessTokenMinutes = 0,
            RefreshTokenDays = 1000,
            MaxFailedAttempts = 0,
            LockoutMinutes = 0,
            AllowedEmailDomains = ["not a domain"],
            ResetUrlTemplate = "https://x.example.com/no-placeholder"
        }));
        Assert.Equal(
            ["password_min_length", "access_token_minutes", "refresh_token_days", "max_failed_attempts",
             "lockout_minutes", "reset_url_template", "allowed_email_domains"],
            ex.Errors.Select(e => e.Pointer).ToArray());
    }

    [Fact]
    public async Task PasswordMinLength_FromSettings_IsEnforced()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await h.SaveSettingsAsync(app, s => s with { PasswordMinLength = 24 });
        var ex = await Assert.ThrowsAsync<ValidationException>(() => h.RegisterAsync(app, "len@example.com", "only-twenty-chars-long"));
        Assert.Contains("24", ex.Errors.Single().Message);
    }
}
