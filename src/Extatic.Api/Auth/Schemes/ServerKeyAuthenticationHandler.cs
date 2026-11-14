using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Extatic.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Extatic.Api.Auth.Schemes;

/// <summary>
/// Authenticates server-to-server calls on <c>api/{app_public_id}/...</c> routes using the App's
/// secret API key in <see cref="HeaderName"/>. The SHA-256 (lowercase hex) of the key must equal
/// <c>App.ApiKeyHash</c> for the app named in the URL. Sets <c>HttpContext.Items["CurrentApp"]</c>.
/// Only used via <c>[Authorize(Policy = PolicyNames.AppServerKey)]</c>; never a default scheme.
/// </summary>
public class ServerKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-Extatic-Secret-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult();
        if (!key.StartsWith("exk_", StringComparison.Ordinal) || key.Length > 256)
            return AuthenticateResult.Fail("Invalid secret key");

        var segments = Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var publicId = segments?.ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(publicId) || !publicId.StartsWith("app_"))
            return AuthenticateResult.Fail("Missing or invalid app identifier in URL");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.PublicId == publicId);
        if (app is null || string.IsNullOrEmpty(app.ApiKeyHash))
            return AuthenticateResult.Fail("Invalid secret key");

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        byte[] expected;
        try { expected = Convert.FromHexString(app.ApiKeyHash); }
        catch (FormatException) { return AuthenticateResult.Fail("Invalid secret key"); }
        if (!CryptographicOperations.FixedTimeEquals(presented, expected))
            return AuthenticateResult.Fail("Invalid secret key");

        Context.Items["CurrentApp"] = app;
        var identity = new ClaimsIdentity(
        [
            new Claim(AppClaimTypes.AppId, app.Id.ToString()),
            new Claim(AppClaimTypes.ServerKey, "true")
        ], AuthSchemes.ServerKey);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.ServerKey));
    }
}
