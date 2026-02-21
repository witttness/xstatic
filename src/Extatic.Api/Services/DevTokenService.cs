using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Extatic.Api.Services;

public class DevTokenService(IConfiguration configuration)
{
    public async Task<(AppUser User, string Token)> IssueAsync(
        AppUserService appUserService,
        Guid appId,
        string? email,
        string? displayName)
    {
        var providerUserId = email ?? Guid.NewGuid().ToString();
        var user = await appUserService.UpsertAsync(
            appId, "dev", providerUserId, email, displayName, null);

        var token = CreateToken(user);
        return (user, token);
    }

    private string CreateToken(AppUser user)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required");
        var issuer = configuration["Jwt:Issuer"] ?? "extatic";
        var audience = configuration["Jwt:Audience"] ?? "extatic-client";

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(AppClaimTypes.AppUserId, user.Id.ToString()),
            new Claim(AppClaimTypes.AppId, user.AppId.ToString()),
            new Claim(AppClaimTypes.Provider, user.Provider)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
