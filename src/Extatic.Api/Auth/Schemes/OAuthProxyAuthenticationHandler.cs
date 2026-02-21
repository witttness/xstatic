using System.Security.Claims;
using System.Text.Encodings.Web;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Extatic.Api.Auth.Schemes;

public class OAuthProxyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var externalId = Request.Headers["X-Forwarded-User"].FirstOrDefault();
        var email = Request.Headers["X-Forwarded-Email"].FirstOrDefault();

        if (string.IsNullOrEmpty(externalId) || string.IsNullOrEmpty(email))
            return AuthenticateResult.Fail("Missing X-Forwarded-User or X-Forwarded-Email headers");

        using var scope = scopeFactory.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var user = await userService.UpsertAsync(externalId, email);

        var claims = new[]
        {
            new Claim(AppClaimTypes.UserId, user.Id.ToString()),
            new Claim(AppClaimTypes.UserEmail, user.Email),
            new Claim(ClaimTypes.Name, user.Name)
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.OAuthProxy);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthSchemes.OAuthProxy));
    }
}
