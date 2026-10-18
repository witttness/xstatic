using Extatic.Api.Data;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Extatic.Api.Auth.Cors;

/// <summary>
/// Resolves CORS policy per request:
/// - Client API paths (/api/app_{id}/...) → restricted to the App's AllowedOrigins,
///   or permissive if the developer hasn't configured any yet.
/// - Platform API paths (/api/apps/...) → permissive (dashboard + dev tooling).
/// </summary>
public class DynamicCorsPolicy(IServiceScopeFactory scopeFactory, IMemoryCache cache) : ICorsPolicyProvider
{
    private static readonly CorsPolicy PermissivePolicy = new CorsPolicyBuilder()
        .AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
        .Build();

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var second = segments?.ElementAtOrDefault(1) ?? "";

        // Platform API — preserve permissive behaviour
        if (!second.StartsWith("app_"))
            return PermissivePolicy;

        var origins = await cache.GetOrCreateAsync($"cors:{second}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Apps
                .Where(a => a.PublicId == second)
                .Select(a => a.AllowedOrigins)
                .FirstOrDefaultAsync();
        });

        if (origins is null) return null;          // unknown app → deny preflight
        if (origins.Length == 0) return PermissivePolicy; // not yet configured

        return new CorsPolicyBuilder(origins)
            .AllowAnyMethod()
            .WithHeaders("Content-Type", "Authorization")
            .Build();
    }
}
