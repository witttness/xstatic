namespace Extatic.Api.Auth;

public static class RateLimitPolicyNames
{
    public const string ClientReads   = "client-reads";
    public const string ClientWrites  = "client-writes";
    public const string ClientUploads = "client-uploads";
    /// <summary>Sliding window 10/min per App + IP, for unauthenticated public writes (form posts, comments, registrations).</summary>
    public const string ClientAnonymousWrites = "client-anon-writes";
}
