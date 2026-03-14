using System.Threading.RateLimiting;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace Extatic.Api.Auth.RateLimiting;

/// <summary>
/// Fixed window — 300 requests/minute per IP per App.
/// Used for read operations where no AppUser JWT is required.
/// </summary>
public class ClientReadsPolicy : IRateLimiterPolicy<string>
{
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var app = httpContext.Items["CurrentApp"] as App;
        var ip  = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{app?.PublicId ?? "unknown"}:{ip}";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
}
