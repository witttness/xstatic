using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Services;

public class DevTokenService(IConfiguration configuration)
{
    private readonly AppUserTokenService tokens = new(configuration);

    public async Task<(AppUser User, string Token)> IssueAsync(
        AppUserService appUserService,
        Guid appId,
        string? email,
        string? displayName)
    {
        var providerUserId = email ?? Guid.NewGuid().ToString();
        var user = await appUserService.UpsertAsync(
            appId, "dev", providerUserId, email, displayName, null);

        var token = tokens.CreateAccessToken(user);
        return (user, token);
    }
}
