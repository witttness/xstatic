using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Authentication;

/// <summary>Hourly purge of auth tokens that expired, were used or were revoked more than 7 days ago.</summary>
public class AuthTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<AuthTokenCleanupService> logger) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await PurgeAsync(db, time.GetUtcNow().UtcDateTime, stoppingToken);
                if (deleted > 0) logger.LogInformation("Purged {Count} expired auth tokens", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Auth token cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Rotated (used) refresh tokens are kept until they expire so that reuse of a stolen token is still
    /// detected and revokes its family; every other dead token goes 7 days after it died.
    /// </summary>
    public static Task<int> PurgeAsync(AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var cutoff = now - Retention;
        return db.AppUserAuthTokens
            .Where(t => t.ExpiresAt < cutoff
                || (t.RevokedAt != null && t.RevokedAt < cutoff)
                || (t.Purpose != AuthTokenPurpose.Refresh && t.UsedAt != null && t.UsedAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }
}
