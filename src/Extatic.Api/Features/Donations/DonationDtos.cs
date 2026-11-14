using Extatic.Api.Services;

namespace Extatic.Api.Features.Donations;

// ---- Provider config ------------------------------------------------------------------------

/// <summary>Never contains a secret: only presence flags and the last 4 characters.</summary>
public record ProviderConfigDto(
    string Provider,
    bool Configured,
    string? PublishableKey,
    bool HasSecretKey,
    string? SecretKeyLast4,
    bool HasWebhookSecret,
    string? WebhookSecretLast4,
    bool IsLiveMode,
    bool IsEnabled,
    string WebhookUrl,
    IReadOnlyList<string> WebhookEvents,
    DateTime? UpdatedAt);

/// <summary>Omitted (null) secret fields keep their stored value. Empty string publishable_key clears it.</summary>
public record PutProviderConfigRequest(string? PublishableKey, string? SecretKey, string? WebhookSecret, bool? IsEnabled);

// ---- Campaigns ------------------------------------------------------------------------------

public record CampaignRequest(
    string Name,
    string Slug,
    string? Description,
    string Currency,
    long? GoalAmountMinor,
    long[]? SuggestedAmountsMinor,
    long? MinAmountMinor,
    long? MaxAmountMinor,
    bool? AllowCustomAmount,
    bool? AllowRecurring,
    string SuccessUrl,
    string CancelUrl,
    bool? ShowDonorList,
    string[]? NotifyEmails,
    bool? IsActive,
    DateTime? StartsAt,
    DateTime? EndsAt);

public record CampaignDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string Currency,
    long? GoalAmountMinor,
    long[] SuggestedAmountsMinor,
    long MinAmountMinor,
    long MaxAmountMinor,
    bool AllowCustomAmount,
    bool AllowRecurring,
    string SuccessUrl,
    string CancelUrl,
    bool ShowDonorList,
    string[] NotifyEmails,
    bool IsActive,
    DateTime? StartsAt,
    DateTime? EndsAt,
    long RaisedMinor,
    int DonorCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CampaignTotals(long RaisedMinor, int DonorCount)
{
    public static readonly CampaignTotals Zero = new(0, 0);
}

public record RecentDonorDto(string Name, long AmountMinor, string Currency, string? Message, DateTime DonatedAt);

public record ClientCampaignDto(
    string Slug,
    string Name,
    string? Description,
    string Currency,
    long? GoalMinor,
    long RaisedMinor,
    int DonorCount,
    long[] SuggestedAmountsMinor,
    long MinAmountMinor,
    long MaxAmountMinor,
    bool AllowCustomAmount,
    bool AllowRecurring,
    DateTime? StartsAt,
    DateTime? EndsAt,
    IReadOnlyList<RecentDonorDto>? RecentDonors);

// ---- Checkout / donations -------------------------------------------------------------------

public record ClientCheckoutRequest(
    string? CampaignSlug,
    long AmountMinor,
    string? Currency,
    string? Interval,
    string? DonorName,
    string? DonorEmail,
    bool? IsAnonymous,
    string? Message);

public record CheckoutResponse(Guid DonationId, string CheckoutUrl);

public record DonationStatusDto(string Status);

public record DonationDto(
    Guid Id,
    Guid? CampaignId,
    string? CampaignSlug,
    string? CampaignName,
    Guid? AppUserId,
    long AmountMinor,
    long RefundedAmountMinor,
    string Currency,
    string Interval,
    string Status,
    string? DonorName,
    string? DonorEmail,
    bool IsAnonymous,
    string? Message,
    string Provider,
    string? ProviderSessionId,
    string? ProviderPaymentId,
    string? ProviderSubscriptionId,
    DateTime? CompletedAt,
    DateTime CreatedAt);

public record RefundDonationRequest(long? AmountMinor);

public record CurrencyAmount(string Currency, long AmountMinor, int DonationCount);

public record DonationSummaryDto(
    IReadOnlyList<CurrencyAmount> TotalRaised,
    IReadOnlyList<CurrencyAmount> Last30Days,
    int RecurringCount,
    int PendingCount);

public record DonationQuery(
    string? Status,
    Guid? CampaignId,
    DateTime? From,
    DateTime? To,
    string? Q);

public static class DonationApiValues
{
    public static string ToApi(this DonationStatus status) => status switch
    {
        DonationStatus.Pending => "pending",
        DonationStatus.Succeeded => "succeeded",
        DonationStatus.Failed => "failed",
        DonationStatus.Expired => "expired",
        DonationStatus.Refunded => "refunded",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static string ToApi(this DonationInterval interval) => interval switch
    {
        DonationInterval.OneTime => "one_time",
        DonationInterval.Monthly => "monthly",
        DonationInterval.Yearly => "yearly",
        _ => throw new ArgumentOutOfRangeException(nameof(interval))
    };

    public static DonationInterval ParseInterval(string? value) => value switch
    {
        null or "" or "one_time" => DonationInterval.OneTime,
        "monthly" => DonationInterval.Monthly,
        "yearly" => DonationInterval.Yearly,
        _ => throw new ValidationException("Invalid interval",
            [new ValidationError("interval", "Must be one_time, monthly or yearly")])
    };

    public static DonationStatus? ParseStatus(string? value) => value switch
    {
        null or "" => null,
        "pending" => DonationStatus.Pending,
        "succeeded" => DonationStatus.Succeeded,
        "failed" => DonationStatus.Failed,
        "expired" => DonationStatus.Expired,
        "refunded" => DonationStatus.Refunded,
        _ => throw new BadRequestException("status must be pending, succeeded, failed, expired or refunded")
    };

    public static CampaignDto ToDto(this DonationCampaign c, CampaignTotals totals) => new(
        c.Id, c.Name, c.Slug, c.Description, c.Currency, c.GoalAmountMinor, c.SuggestedAmountsMinor,
        c.MinAmountMinor, c.MaxAmountMinor, c.AllowCustomAmount, c.AllowRecurring, c.SuccessUrl, c.CancelUrl,
        c.ShowDonorList, c.NotifyEmails, c.IsActive, c.StartsAt, c.EndsAt, totals.RaisedMinor, totals.DonorCount,
        c.CreatedAt, c.UpdatedAt);

    public static DonationDto ToDto(this Donation d) => new(
        d.Id, d.CampaignId, d.Campaign?.Slug, d.Campaign?.Name, d.AppUserId, d.AmountMinor, d.RefundedAmountMinor,
        d.Currency, d.Interval.ToApi(), d.Status.ToApi(), d.DonorName, d.DonorEmail, d.IsAnonymous, d.Message,
        d.Provider, d.ProviderSessionId, d.ProviderPaymentId, d.ProviderSubscriptionId, d.CompletedAt, d.CreatedAt);
}
