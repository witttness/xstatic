using Extatic.Api.Data;
using Extatic.Api.Notifications;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Members;

/// <summary>
/// Expires lapsed memberships and sends renewal reminders. Safe to run concurrently on several
/// replicas: each membership is claimed with a conditional UPDATE inside the transaction that
/// stages its emails/webhooks, so a second runner's claim matches zero rows.
/// </summary>
public class MembershipMaintenance(
    AppDbContext db,
    WebhookDispatcher webhooks,
    IEmailQueue emailQueue,
    TimeProvider time)
{
    private const int BatchSize = 200;

    public record Result(int Expired, int RemindersSent);

    public async Task<Result> RunAsync(CancellationToken ct = default)
        => new(await ExpireDueAsync(ct), await SendRemindersAsync(ct));

    public async Task<int> ExpireDueAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expired = 0;
        var seen = new HashSet<Guid>();
        while (true)
        {
            var due = await db.Memberships.AsNoTracking()
                .Where(m => m.Status == MembershipStatus.Active && m.ExpiresAt != null && m.ExpiresAt <= now
                            && !seen.Contains(m.Id))
                .OrderBy(m => m.ExpiresAt)
                .Select(m => new { m.Id, m.AppId })
                .Take(BatchSize).ToListAsync(ct);
            if (due.Count == 0) break;
            foreach (var d in due)
            {
                seen.Add(d.Id);
                if (await ExpireOneAsync(d.AppId, d.Id, now, ct)) expired++;
            }
            db.ChangeTracker.Clear();
        }
        return expired;
    }

    private async Task<bool> ExpireOneAsync(Guid appId, Guid id, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Memberships
            .Where(m => m.AppId == appId && m.Id == id && m.Status == MembershipStatus.Active
                        && m.ExpiresAt != null && m.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MembershipStatus.Expired)
                .SetProperty(m => m.UpdatedAt, DateTime.UtcNow), ct);
        if (claimed == 0) return false;

        var m = await db.Memberships.AsNoTracking().Include(x => x.Plan).Include(x => x.AppUser)
            .FirstAsync(x => x.AppId == appId && x.Id == id, ct);
        db.MembershipEvents.Add(new MembershipEvent
        {
            AppId = appId,
            MembershipId = id,
            Type = "expired",
            FromStatus = MembershipStatus.Active,
            ToStatus = MembershipStatus.Expired,
        });
        await webhooks.StageAsync(db, appId, "member.expired", m.ToWebhookData(m.Plan, m.AppUser));
        if (EmailQueue.IsValidAddress(m.AppUser.Email))
        {
            var appName = await db.Apps.Where(a => a.Id == appId).Select(a => a.Name).FirstAsync(ct);
            emailQueue.Stage(db, appId, MemberEmails.Expired(m.AppUser.Email!.Trim(),
                MembershipService.EmailContext(appName, m.Plan, m.AppUser, m)));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> SendRemindersAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var horizon = now.AddDays(MembersLimits.MaxReminderDay);
        var sent = 0;
        var settingsCache = new Dictionary<Guid, int[]>();
        var skip = 0;
        while (true)
        {
            var query = db.Memberships.AsNoTracking()
                .Where(m => m.Status == MembershipStatus.Active && m.ExpiresAt != null
                            && m.ExpiresAt > now && m.ExpiresAt <= horizon);
            var batch = await query.OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.AppId, m.ExpiresAt, m.RemindersSent })
                .Skip(skip).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            skip += batch.Count;

            foreach (var row in batch)
            {
                if (!settingsCache.TryGetValue(row.AppId, out var offsets))
                {
                    offsets = await db.MemberSettings.Where(s => s.AppId == row.AppId)
                                  .Select(s => s.RenewalReminderDays).FirstOrDefaultAsync(ct)
                              ?? MemberSettings.DefaultReminderDays;
                    settingsCache[row.AppId] = offsets;
                }
                var expiresAt = row.ExpiresAt!.Value;
                // Offsets whose moment has arrived and that were not sent this term. When several are
                // due at once (e.g. a short term), send only the nearest one but mark all as sent.
                var dueOffsets = offsets.Where(d => now >= expiresAt.AddDays(-d) && !row.RemindersSent.Contains(d))
                    .ToArray();
                if (dueOffsets.Length == 0) continue;
                if (await RemindOneAsync(row.AppId, row.Id, expiresAt, dueOffsets, now, ct)) sent++;
            }
            db.ChangeTracker.Clear();
        }
        return sent;
    }

    private async Task<bool> RemindOneAsync(Guid appId, Guid id, DateTime expiresAt, int[] dueOffsets, DateTime clockNow, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        // Atomic claim: only succeeds if none of these offsets were recorded by someone else and the
        // term is unchanged (a renewal moves ExpiresAt and resets RemindersSent).
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE memberships SET "RemindersSent" = "RemindersSent" || {dueOffsets}, "UpdatedAt" = {now}
            WHERE "Id" = {id} AND "AppId" = {appId} AND "Status" = 'Active' AND "ExpiresAt" = {expiresAt}
              AND NOT ("RemindersSent" && {dueOffsets})
            """, ct);
        if (claimed == 0) return false;

        var m = await db.Memberships.AsNoTracking().Include(x => x.Plan).Include(x => x.AppUser)
            .FirstAsync(x => x.AppId == appId && x.Id == id, ct);
        var offset = dueOffsets.Min();
        var daysLeft = Math.Max(1, (int)Math.Ceiling((expiresAt - clockNow).TotalDays));
        db.MembershipEvents.Add(new MembershipEvent
        {
            AppId = appId,
            MembershipId = id,
            Type = "renewal_reminder",
            Detail = $"{offset}-day reminder ({daysLeft} days left)",
        });
        if (EmailQueue.IsValidAddress(m.AppUser.Email))
        {
            var appName = await db.Apps.Where(a => a.Id == appId).Select(a => a.Name).FirstAsync(ct);
            emailQueue.Stage(db, appId, MemberEmails.RenewalReminder(m.AppUser.Email!.Trim(),
                MembershipService.EmailContext(appName, m.Plan, m.AppUser, m), daysLeft));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}

/// <summary>Runs <see cref="MembershipMaintenance"/> hourly.</summary>
public class MembershipMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<MembershipMaintenanceWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<MembershipMaintenance>().RunAsync(stoppingToken);
                    if (result.Expired > 0 || result.RemindersSent > 0)
                        logger.LogInformation("Memberships: expired {Expired}, reminders {Reminders}",
                            result.Expired, result.RemindersSent);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Membership maintenance failed");
                }
            } while (await WaitAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<bool> WaitAsync(CancellationToken ct)
    {
        await Task.Delay(Interval, ct);
        return true;
    }
}
