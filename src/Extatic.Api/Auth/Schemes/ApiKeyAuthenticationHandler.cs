using System.Security.Claims;
using System.Text.Encodings.Web;
using Extatic.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Extatic.Api.Auth.Schemes;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            return AuthenticateResult.Fail("Missing X-Api-Key header");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keyHash = ComputeHash(apiKey);
        var app = await db.Apps.FirstOrDefaultAsync(a => a.ApiKeyHash == keyHash);
        if (app is null)
            return AuthenticateResult.Fail("Invalid API key");

        Context.Items["CurrentApp"] = app;

        var claims = new[]
        {
            new Claim(AppClaimTypes.AppId, app.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.ApiKey);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthSchemes.ApiKey));
    }

    private static string ComputeHash(string apiKey)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
