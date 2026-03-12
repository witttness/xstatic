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
        var publicId = Request.Headers["X-App-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(publicId))
            return AuthenticateResult.Fail("Missing X-App-Id header");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var app = await db.Apps.FirstOrDefaultAsync(a => a.PublicId == publicId);
        if (app is null)
            return AuthenticateResult.Fail("Invalid App ID");

        Context.Items["CurrentApp"] = app;

        var claims = new[]
        {
            new Claim(AppClaimTypes.AppId, app.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.ApiKey);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthSchemes.ApiKey));
    }
}
