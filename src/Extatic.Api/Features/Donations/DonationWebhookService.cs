using Extatic.Api.Data;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Donations;

public enum PaymentWebhookOutcome
{
    Processed,
    Duplicate,
    Ignored
}

/// <summary>
/// Handles provider → Extatic webhooks for one tenant. The signature is verified with THIS tenant's
/// webhook secret; donations are matched by id AND AppId (and session / payment id), so an event
/// from another tenant's account can never touch this tenant's data.
/// </summary>
public class DonationWebhookService(
    AppDbContext db,
    PaymentProviderRegistry registry,
    PaymentProviderConfigService configs,
    DonationNotifier notifier,
    ILogger<DonationWebhookService> logger)
{
    /// <summary>Throws <see cref="WebhookSignatureException"/> (→ 400) for unverifiable requests.</summary>
    public async Task<PaymentWebhookOutcome> HandleAsync(Guid appId, string providerName, string rawBody,
        IHeaderDictionary headers)
    {
        var provider = registry.Get(providerName);
        var config = await configs.FindAsync(appId, provider.Name)
            ?? throw new WebhookSignatureException("Provider not configured");
        PaymentCredentials credentials;
        try
        {
            credentials = configs.Credentials(config);
        }
        catch (PaymentProviderException)
        {
            throw new WebhookSignatureException("Stored webhook secret unreadable");
        }

        var evt = provider.ParseWebhook(credentials, rawBody, headers);

        if (await db.PaymentWebhookEvents.AnyAsync(e =>
                e.AppId == appId && e.Provider == provider.Name && e.ProviderEventId == evt.EventId))
            return PaymentWebhookOutcome.Duplicate;

        await using var tx = await db.Database.BeginTransactionAsync();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            AppId = appId,
            Provider = provider.Name,
            ProviderEventId = evt.EventId,
            Type = evt.RawType.Length > 100 ? evt.RawType[..100] : evt.RawType,
            ProcessedAt = DateTime.UtcNow
        });
        try
        {
            // Claims the event id first: a concurrent duplicate blocks on the unique index here and
            // then fails, so only one delivery ever applies the event.
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DonationDb.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync();
            db.ChangeTracker.Clear();
            return PaymentWebhookOutcome.Duplicate;
        }

        var outcome = await ApplyAsync(appId, provider.Name, evt);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return outcome;
    }

    private async Task<PaymentWebhookOutcome> ApplyAsync(Guid appId, string provider, ProviderWebhookEvent evt)
    {
        switch (evt.Kind)
        {
            case ProviderWebhookEventKind.CheckoutCompleted:
            case ProviderWebhookEventKind.AsyncPaymentSucceeded:
            case ProviderWebhookEventKind.AsyncPaymentFailed:
            case ProviderWebhookEventKind.CheckoutExpired:
                return await ApplySessionEventAsync(appId, provider, evt);
            case ProviderWebhookEventKind.ChargeRefunded:
                return await ApplyRefundAsync(appId, provider, evt);
            default:
                return PaymentWebhookOutcome.Ignored;
        }
    }

    private async Task<PaymentWebhookOutcome> ApplySessionEventAsync(Guid appId, string provider, ProviderWebhookEvent evt)
    {
        if (evt.DonationId is null || string.IsNullOrEmpty(evt.SessionId)) return PaymentWebhookOutcome.Ignored;
        var donation = await DonationDb.LockDonationAsync(db, appId, evt.DonationId.Value);
        if (donation is null || donation.Provider != provider || donation.ProviderSessionId != evt.SessionId)
        {
            logger.LogWarning("Ignoring {EventType} {EventId}: no matching donation for this app", evt.RawType, evt.EventId);
            return PaymentWebhookOutcome.Ignored;
        }
        if (donation.Status != DonationStatus.Pending) return PaymentWebhookOutcome.Ignored;

        switch (evt.Kind)
        {
            case ProviderWebhookEventKind.CheckoutExpired:
                donation.Status = DonationStatus.Expired;
                return PaymentWebhookOutcome.Processed;

            case ProviderWebhookEventKind.AsyncPaymentFailed:
                donation.Status = DonationStatus.Failed;
                donation.ProviderPaymentId ??= evt.PaymentId;
                await notifier.StageFailedAsync(db, donation);
                return PaymentWebhookOutcome.Processed;

            default: // completed / async succeeded
                if (evt.AmountMinor != donation.AmountMinor
                    || !string.Equals(evt.Currency, donation.Currency, StringComparison.Ordinal))
                {
                    logger.LogWarning("Donation {DonationId}: provider amount/currency does not match the recorded donation; marking failed",
                        donation.Id);
                    donation.Status = DonationStatus.Failed;
                    return PaymentWebhookOutcome.Ignored;
                }
                donation.ProviderPaymentId ??= evt.PaymentId;
                donation.ProviderSubscriptionId ??= evt.SubscriptionId;
                if (evt.Kind == ProviderWebhookEventKind.CheckoutCompleted && !evt.IsPaid)
                    return PaymentWebhookOutcome.Processed; // async payment method: wait for async_payment_*
                donation.Status = DonationStatus.Succeeded;
                donation.CompletedAt = DateTime.UtcNow;
                await notifier.StageSucceededAsync(db, donation);
                return PaymentWebhookOutcome.Processed;
        }
    }

    private async Task<PaymentWebhookOutcome> ApplyRefundAsync(Guid appId, string provider, ProviderWebhookEvent evt)
    {
        if (string.IsNullOrEmpty(evt.PaymentId) || evt.RefundedAmountMinor is not { } refunded) return PaymentWebhookOutcome.Ignored;
        var donation = await DonationDb.LockDonationByPaymentAsync(db, appId, provider, evt.PaymentId);
        if (donation is null || donation.Status is not (DonationStatus.Succeeded or DonationStatus.Refunded))
            return PaymentWebhookOutcome.Ignored;
        if (!string.Equals(evt.Currency, donation.Currency, StringComparison.Ordinal))
        {
            logger.LogWarning("Donation {DonationId}: refund currency does not match; ignored", donation.Id);
            return PaymentWebhookOutcome.Ignored;
        }
        var total = Math.Min(refunded, donation.AmountMinor);
        if (total <= donation.RefundedAmountMinor) return PaymentWebhookOutcome.Ignored; // already recorded (e.g. dashboard refund)
        donation.RefundedAmountMinor = total;
        if (total == donation.AmountMinor) donation.Status = DonationStatus.Refunded;
        await notifier.StageRefundedAsync(db, donation);
        return PaymentWebhookOutcome.Processed;
    }
}
