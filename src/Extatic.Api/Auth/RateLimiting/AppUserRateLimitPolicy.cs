using System.Threading.RateLimiting;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Auth.RateLimiting;

/// <summary>
/// Sliding window — 60 requests/minute per AppUser per App.
/// Used for write operations (POST, PUT, DELETE items and attachments).
/// </summary>
public class ClientWritesPolicy : IRateLimiterPolicy<string>
{
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var app    = httpContext.Items["CurrentApp"] as App;
        var userId = httpContext.User.FindFirst(AppClaimTypes.AppUserId)?.Value ?? "anon";
        var key    = $"{app?.PublicId ?? "unknown"}:{userId}";

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit       = 60,
            Window            = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,   // 10-second buckets
            QueueLimit        = 0,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
}
