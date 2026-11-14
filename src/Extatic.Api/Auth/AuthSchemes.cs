namespace Extatic.Api.Auth;

public static class AuthSchemes
{
    public const string SmartSelector = "SmartSelector";
    public const string OAuthProxy = "OAuthProxy";
    public const string ApiKey = "ApiKey";
    public const string AppUser = "AppUser";
    /// <summary>Server-to-server: <c>X-Extatic-Secret-Key: exk_...</c> on <c>api/{app_public_id}/...</c> routes.</summary>
    public const string ServerKey = "ServerKey";
}
