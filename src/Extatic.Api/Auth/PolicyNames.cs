namespace Extatic.Api.Auth;

public static class PolicyNames
{
    public const string PlatformUser = "PlatformUser";
    public const string AppAnyAccess = "AppAnyAccess";
    public const string AppOwnerOrAdmin = "AppOwnerOrAdmin";
    public const string AppOwnerOrEditor = "AppOwnerOrEditor";
    public const string AppOwnerOnly = "AppOwnerOnly";
    public const string AuthenticatedAppUser = "AuthenticatedAppUser";
}
