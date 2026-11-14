using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Donations;

public enum DonationInterval
{
    OneTime,
    Monthly,
    Yearly
}

public enum DonationStatus
{
    Pending,
    Succeeded,
    Failed,
    Expired,
    Refunded
}

/// <summary>A tenant's own payment provider account (one per App + provider). Keys are stored protected.</summary>
public class PaymentProviderConfig : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? PublishableKey { get; set; }
    public string SecretKeyProtected { get; set; } = string.Empty;
    /// <summary>Last 4 characters of the secret key, for display only.</summary>
    public string SecretKeyLast4 { get; set; } = string.Empty;
    public string? WebhookSecretProtected { get; set; }
    public string? WebhookSecretLast4 { get; set; }
    public bool IsLiveMode { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}

public class DonationCampaign : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>ISO 4217, lowercase.</summary>
    public string Currency { get; set; } = "usd";
    public long? GoalAmountMinor { get; set; }
    public long[] SuggestedAmountsMinor { get; set; } = [];
    public long MinAmountMinor { get; set; }
    public long MaxAmountMinor { get; set; }
    public bool AllowCustomAmount { get; set; } = true;
    public bool AllowRecurring { get; set; }
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public bool ShowDonorList { get; set; }
    public string[] NotifyEmails { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}

public class Donation : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? AppUserId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DonationInterval Interval { get; set; }
    public DonationStatus Status { get; set; } = DonationStatus.Pending;
    public string? DonorName { get; set; }
    public string? DonorEmail { get; set; }
    public bool IsAnonymous { get; set; }
    public string? Message { get; set; }
    public string Provider { get; set; } = string.Empty;
    /// <summary>Null only between the insert and the provider call (or when that call failed).</summary>
    public string? ProviderSessionId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public long RefundedAmountMinor { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string IpHash { get; set; } = string.Empty;

    public App App { get; set; } = null!;
    public DonationCampaign? Campaign { get; set; }
    public AppUser? AppUser { get; set; }

    /// <summary>Amount that still counts towards totals.</summary>
    public long NetAmountMinor => AmountMinor - RefundedAmountMinor;
}

/// <summary>Processed provider webhook events; the unique (AppId, Provider, ProviderEventId) index makes delivery idempotent.</summary>
public class PaymentWebhookEvent : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public App App { get; set; } = null!;
}
