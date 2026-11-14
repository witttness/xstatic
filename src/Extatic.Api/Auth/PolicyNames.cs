namespace Extatic.Api.Auth;

public static class PolicyNames
{
    public const string PlatformUser = "PlatformUser";
    public const string AppAnyAccess = "AppAnyAccess";
    public const string AppOwnerOrAdmin = "AppOwnerOrAdmin";
    public const string AppOwnerOrEditor = "AppOwnerOrEditor";
    public const string AppOwnerOnly = "AppOwnerOnly";
    public const string AuthenticatedAppUser = "AuthenticatedAppUser";

    /// <summary>Client API: valid app id required; AppUser bearer token optional (use ClientControllerBase.AppUserId).</summary>
    public const string ClientAnonymousOrAppUser = "ClientAnonymousOrAppUser";

    /// <summary>Client-API-shaped route called server-to-server with the App's secret key (X-Extatic-Secret-Key).</summary>
    public const string AppServerKey = "AppServerKey";

    /// <summary>Platform user whose email is listed in Platform:AdminEmails.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
