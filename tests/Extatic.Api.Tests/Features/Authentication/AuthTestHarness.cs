using System.Text.RegularExpressions;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Authentication;
using Extatic.Api.Notifications;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Authentication;

/// <summary>Controllable clock for token expiry / lockout tests.</summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now = now.Add(by);
}

/// <summary>
/// Builds services/controllers the way a request would: a fresh AppDbContext per call, shared clock.
/// </summary>
public sealed partial class AuthTestHarness(ApiTestFixture fixture)
{
    public const string Password = "correct-horse-battery";
    public MutableTimeProvider Time { get; } = new();

    public async Task<T> Run<T>(Func<PasswordAuthService, Task<T>> action)
    {
        await using var db = fixture.CreateContext();
        return await action(Service(db));
    }

    public async Task Run(Func<PasswordAuthService, Task> action)
    {
        await using var db = fixture.CreateContext();
        await action(Service(db));
    }

    public PasswordAuthService Service(AppDbContext db) => new(db, new AuthSettingsService(db),
        TestServices.EmailQueue(), TestServices.WebhookDispatcher(fixture), TestServices.AppUserTokenService(), Time);

    /// <summary>Runs a client controller action on a fresh context (CurrentApp = <paramref name="app"/>).</summary>
    public async Task<T> Client<T>(App app, Func<ClientPasswordAuthController, Task<T>> action, Guid? appUserId = null,
        Guid? tokenAppId = null)
    {
        await using var db = fixture.CreateContext();
        var controller = new ClientPasswordAuthController(Service(db))
        {
            ControllerContext = TestContexts.Client(app, appUserId, tokenAppId: tokenAppId)
        };
        return await action(controller);
    }

    public async Task<T> Platform<T>(App app, Guid userId, Func<PlatformAuthController, Task<T>> action)
    {
        await using var db = fixture.CreateContext();
        var controller = new PlatformAuthController(new AuthSettingsService(db), Service(db))
        {
            ControllerContext = TestContexts.Platform(app, userId)
        };
        return await action(controller);
    }

    public Task SaveSettingsAsync(App app, Func<UpdateAuthSettingsRequest, UpdateAuthSettingsRequest>? change = null)
    {
        var defaults = new UpdateAuthSettingsRequest(true, true, 10, 60, 30, 5, 15, null, null, null);
        return RunSettings(s => s.UpdateAsync(app, change is null ? defaults : change(defaults)));
    }

    private async Task RunSettings(Func<AuthSettingsService, Task> action)
    {
        await using var db = fixture.CreateContext();
        await action(new AuthSettingsService(db));
    }

    public Task RegisterAsync(App app, string email, string password = Password)
        => Run(s => s.RegisterAsync(app, new RegisterRequest(email, password, "Jo"), "iphash"));

    public Task<AuthTokensResponse> LoginAsync(App app, string email, string password = Password)
        => Run(s => s.LoginAsync(app, new LoginRequest(email, password), "iphash"));

    /// <summary>Registers and verifies via the emailed token; returns the user.</summary>
    public async Task<AppUser> RegisterVerifiedAsync(App app, string email, string password = Password)
    {
        await RegisterAsync(app, email, password);
        var token = await LatestTokenAsync(app, email, AuthEmails.CategoryVerification);
        return await Run(s => s.VerifyEmailAsync(app, token));
    }

    public async Task<List<EmailOutboxMessage>> EmailsAsync(App app, string to, string? category = null)
    {
        await using var db = fixture.CreateContext();
        return await db.EmailOutbox
            .Where(m => m.AppId == app.Id && m.ToAddress == to && (category == null || m.Category == category))
            .OrderBy(m => m.CreatedAt).ToListAsync();
    }

    /// <summary>Extracts the token from the newest email (link "?token=" or "Your code:").</summary>
    public async Task<string> LatestTokenAsync(App app, string to, string category)
    {
        var mail = (await EmailsAsync(app, to, category)).Last();
        var match = TokenRegex().Match(mail.TextBody);
        Assert.True(match.Success, $"No token in: {mail.TextBody}");
        return Uri.UnescapeDataString(match.Groups["t"].Value);
    }

    [GeneratedRegex(@"(?:Your code: |token=)(?<t>[A-Za-z0-9_%-]+)")]
    private static partial Regex TokenRegex();

    public static int? Status(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;
    public static int? Status<T>(ActionResult<T> result) => Status(result.Result!);
    public static object? Value<T>(ActionResult<T> result) => (result.Result as ObjectResult)?.Value;
}
