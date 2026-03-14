using System.Threading.RateLimiting;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Auth.RateLimiting;

/// <summary>
/// Concurrency limiter — 2 simultaneous uploads per AppUser per App.
/// File uploads are the most resource-intensive operation; bounding concurrency
/// is more effective than a request-count window for preventing storage abuse.
/// </summary>
public class ClientUploadsPolicy : IRateLimiterPolicy<string>
{
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var app    = httpContext.Items["CurrentApp"] as App;
        var userId = httpContext.User.FindFirst(AppClaimTypes.AppUserId)?.Value ?? "anon";
        var key    = $"{app?.PublicId ?? "unknown"}:{userId}";

        return RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 2,
            QueueLimit  = 0,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
}
