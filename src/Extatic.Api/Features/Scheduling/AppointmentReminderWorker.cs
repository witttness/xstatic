using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Scheduling;

/// <summary>
/// Stages a reminder email for Confirmed appointments starting within <see cref="Lead"/>. Multi-replica
/// safe and exactly-once: inside a transaction, a conditional UPDATE sets <c>reminder_sent_at</c> only
/// while it is still NULL (a concurrent claimer blocks on the row lock, then matches 0 rows), and the
/// email is staged in the same transaction, so either both the claim and the outbox row commit or neither does.
/// </summary>
public class AppointmentReminderWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<AppointmentReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Lead = TimeSpan.FromHours(24);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
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
                logger.LogError(ex, "Error sending appointment reminders");
            }
        }
    }

    /// <summary>Claims due appointments and stages their reminders. Returns the number claimed by this call.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<SchedulingNotifier>();

        var now = time.GetUtcNow().UtcDateTime;
        var horizon = now + Lead;
        var dueIds = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Confirmed && a.ReminderSentAt == null
                && a.StartsAt > now && a.StartsAt <= horizon)
            .OrderBy(a => a.StartsAt)
            .Select(a => a.Id)
            .Take(100)
            .ToListAsync(ct);

        var claimed = 0;
        foreach (var id in dueIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var updated = await db.Appointments
                .Where(a => a.Id == id && a.Status == AppointmentStatus.Confirmed && a.ReminderSentAt == null && a.StartsAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReminderSentAt, now), ct);
            if (updated == 0) continue; // another replica got it, or it changed

            try
            {
                var appointment = await db.Appointments.AsNoTracking()
                    .Include(a => a.Calendar).Include(a => a.AppointmentType)
                    .FirstAsync(a => a.Id == id, ct);
                notifier.StageCustomerEmail(db, appointment, AppointmentEmailKind.Reminder);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                claimed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Rolled back with the claim; retried on the next pass.
                logger.LogWarning("Reminder for appointment {AppointmentId} failed: {ErrorType}", id, ex.GetType().Name);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return claimed;
    }
}
