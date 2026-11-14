using System.Threading.RateLimiting;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Auth.RateLimiting;

/// <summary>
/// Sliding window — 10 requests/minute per IP per App.
/// Used for unauthenticated public writes (form posts, comments, registrations).
/// </summary>
public class ClientAnonymousWritesPolicy : IRateLimiterPolicy<string>
{
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var app = httpContext.Items["CurrentApp"] as App;
        var ip  = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{app?.PublicId ?? "unknown"}:{ip}";

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit       = 10,
            Window            = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit        = 0,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
}
