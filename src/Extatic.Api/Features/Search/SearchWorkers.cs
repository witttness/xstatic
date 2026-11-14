using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Search;

/// <summary>
/// Runs queued <see cref="SearchReindexJob"/>s. Multi-replica safe: a job is claimed by a conditional
/// UPDATE (pending → running, or running with an expired lease → running) so exactly one replica wins;
/// the lease is extended after every batch, and a crashed replica's job is picked up again once its
/// lease expires. Reindexing is idempotent (content-hash upserts), so a re-run is harmless.
/// </summary>
public class SearchReindexWorker(IServiceScopeFactory scopeFactory, ILogger<SearchReindexWorker> logger)
    : BackgroundService
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing search reindex jobs");
            }
        }
    }

    /// <summary>Claims and runs due jobs. Returns the number of jobs this call ran.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        List<Guid> due;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            due = await db.SearchReindexJobs
                .Where(j => j.Status == ReindexStatuses.Pending
                    || (j.Status == ReindexStatuses.Running && j.LeaseExpiresAt < now))
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .Take(10)
                .ToListAsync(ct);
        }

        var ran = 0;
        foreach (var id in due)
            if (await TryRunAsync(id, ct)) ran++;
        return ran;
    }

    private async Task<bool> TryRunAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var claimed = await db.SearchReindexJobs
            .Where(j => j.Id == jobId && (j.Status == ReindexStatuses.Pending
                || (j.Status == ReindexStatuses.Running && j.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ReindexStatuses.Running)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                .SetProperty(j => j.StartedAt, now)
                .SetProperty(j => j.LeaseExpiresAt, now.Add(Lease))
                .SetProperty(j => j.UpdatedAt, now), ct);
        if (claimed == 0) return false;

        var job = await db.SearchReindexJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, ct);
        if (job.Attempts > MaxAttempts)
        {
            await Finish(db, jobId, ReindexStatuses.Failed, "Gave up after repeated interruptions", ct);
            return true;
        }
        try
        {
            await new SearchReindexer(db).RunAsync(job, (processed, total) =>
                db.SearchReindexJobs.Where(j => j.Id == jobId && j.Status == ReindexStatuses.Running)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Processed, processed)
                        .SetProperty(j => j.Total, total)
                        .SetProperty(j => j.LeaseExpiresAt, DateTime.UtcNow.Add(Lease))
                        .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct), ct);
            await Finish(db, jobId, ReindexStatuses.Completed, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Search reindex job {JobId} failed", jobId);
            var error = $"{ex.GetType().Name}: {ex.Message}";
            await Finish(db, jobId, ReindexStatuses.Failed, error.Length > 2000 ? error[..2000] : error, CancellationToken.None);
        }
        return true;
    }

    private static Task<int> Finish(AppDbContext db, Guid jobId, string status, string? error, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        return db.SearchReindexJobs.Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.Error, error)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.UpdatedAt, now), ct);
    }
}

/// <summary>Purges query-log rows older than <see cref="Retention"/> (hourly).</summary>
public class SearchQueryLogCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<SearchQueryLogCleanupWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await PurgeAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Purged {Count} search query log rows", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error purging search query logs");
            }
        }
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow - Retention;
        return await db.SearchQueryLogs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
