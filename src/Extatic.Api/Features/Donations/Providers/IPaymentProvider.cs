namespace Extatic.Api.Features.Donations.Providers;

/// <summary>
/// A hosted-checkout payment provider. Each tenant uses its own account (<see cref="PaymentCredentials"/>);
/// Extatic never handles card data or funds.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Lowercase identifier used in routes and config rows, e.g. "stripe".</summary>
    string Name { get; }

    /// <summary>Validates tenant-supplied keys and derives live/test mode. Throws ValidationException (422).</summary>
    ProviderKeyInfo ValidateKeys(string? secretKey, string? publishableKey, string? webhookSecret);

    /// <summary>Creates a hosted checkout session. Throws <see cref="PaymentProviderException"/> on any provider failure.</summary>
    Task<CheckoutSession> CreateCheckoutAsync(PaymentCredentials credentials, CheckoutRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies the signature of a raw webhook body with the tenant's webhook secret and normalises it.
    /// Throws <see cref="WebhookSignatureException"/> when the signature is missing/invalid/stale.
    /// </summary>
    ProviderWebhookEvent ParseWebhook(PaymentCredentials credentials, string rawBody, IHeaderDictionary headers);

    /// <summary>Refunds (part of) a captured payment. Throws <see cref="PaymentProviderException"/> on failure.</summary>
    Task<RefundResult> RefundAsync(PaymentCredentials credentials, RefundRequest request, CancellationToken ct = default);
}

public record PaymentCredentials(string SecretKey, string? PublishableKey, string? WebhookSecret);

public record ProviderKeyInfo(bool IsLiveMode);

public record CheckoutRequest(
    Guid DonationId,
    Guid AppId,
    long AmountMinor,
    string Currency,
    DonationInterval Interval,
    string ProductName,
    string SuccessUrl,
    string CancelUrl,
    string? CustomerEmail);

public record CheckoutSession(string SessionId, string CheckoutUrl);

public record RefundRequest(Guid DonationId, string PaymentId, long AmountMinor, string IdempotencyKey);

public record RefundResult(string RefundId, string Status);

public enum ProviderWebhookEventKind
{
    Unknown,
    CheckoutCompleted,
    AsyncPaymentSucceeded,
    AsyncPaymentFailed,
    CheckoutExpired,
    ChargeRefunded
}

/// <summary>Provider-neutral view of a verified webhook event.</summary>
public record ProviderWebhookEvent(
    string EventId,
    string RawType,
    ProviderWebhookEventKind Kind)
{
    public string? SessionId { get; init; }
    /// <summary>Our donation id as echoed by the provider (client_reference_id / metadata).</summary>
    public Guid? DonationId { get; init; }
    public long? AmountMinor { get; init; }
    public string? Currency { get; init; }
    /// <summary>For CheckoutCompleted: true when funds are captured now (false = async payment pending).</summary>
    public bool IsPaid { get; init; }
    public string? PaymentId { get; init; }
    public string? SubscriptionId { get; init; }
    /// <summary>For ChargeRefunded: cumulative amount refunded on the payment.</summary>
    public long? RefundedAmountMinor { get; init; }
}

/// <summary>Any provider API failure. The message is safe to show; it never contains provider bodies or keys.</summary>
public class PaymentProviderException(string message) : Exception(message);

public class WebhookSignatureException(string message) : Exception(message);
