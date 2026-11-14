using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Extatic.Api.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Extatic.Api.Auth;

/// <summary>Issues App-scoped AppUser JWTs accepted by the <see cref="AuthSchemes.AppUser"/> scheme.</summary>
public class AppUserTokenService(IConfiguration configuration)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    public string CreateAccessToken(AppUser user, TimeSpan? lifetime = null)
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
            expires: DateTime.UtcNow.Add(lifetime ?? DefaultLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
