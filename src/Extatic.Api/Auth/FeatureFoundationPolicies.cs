using Extatic.Api.Auth.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace Extatic.Api.Auth;

public static class FeatureFoundationPolicies
{
    /// <summary>Registers ClientAnonymousOrAppUser, AppServerKey and PlatformAdmin.</summary>
    public static AuthorizationOptions AddFeatureFoundationPolicies(this AuthorizationOptions options)
    {
        // ApiKey always authenticates for a valid app id; an AppUser bearer token is optional.
        // Use ClientControllerBase.AppUserId, which ignores tokens issued for another App.
        options.AddPolicy(PolicyNames.ClientAnonymousOrAppUser, policy =>
            policy.RequireAuthenticatedUser()
                  .AddAuthenticationSchemes(AuthSchemes.ApiKey, AuthSchemes.AppUser));

        // Explicit scheme: the policy evaluator authenticates ONLY ServerKey and replaces
        // HttpContext.User, so the SmartSelector default (ApiKey) cannot satisfy this policy.
        options.AddPolicy(PolicyNames.AppServerKey, policy =>
            policy.RequireAuthenticatedUser()
                  .AddAuthenticationSchemes(AuthSchemes.ServerKey)
                  .RequireClaim(AppClaimTypes.ServerKey, "true"));

        options.AddPolicy(PolicyNames.PlatformAdmin, policy =>
            policy.RequireAuthenticatedUser()
                  .AddAuthenticationSchemes(AuthSchemes.OAuthProxy)
                  .AddRequirements(new PlatformAdminRequirement()));
        return options;
    }
}
