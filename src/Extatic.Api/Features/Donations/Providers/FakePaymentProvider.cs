using System.Security.Cryptography;
using System.Text;
using Extatic.Api.Services;

namespace Extatic.Api.Features.Donations.Providers;

/// <summary>
/// In-process provider for local development and tests (name "fake"). Only resolvable when
/// <see cref="PaymentProviderRegistry"/> allows it (Development environment / tests). No network calls.
/// Webhooks: body <c>{"id","type","data":{...}}</c> with the same event types and fields as Stripe,
/// signed with header <c>X-Fake-Signature: hex(HMAC-SHA256(webhookSecret, rawBody))</c>.
/// </summary>
public class FakePaymentProvider : IPaymentProvider
{
    public const string ProviderName = "fake";
    public const string SignatureHeader = "X-Fake-Signature";

    public string Name => ProviderName;

    /// <summary>When set, CreateCheckoutAsync / RefundAsync throw <see cref="PaymentProviderException"/>.</summary>
    public bool Fail { get; set; }

    public List<CheckoutRequest> Checkouts { get; } = [];
    public List<RefundRequest> Refunds { get; } = [];

    public ProviderKeyInfo ValidateKeys(string? secretKey, string? publishableKey, string? webhookSecret)
    {
        if (secretKey is not null && (secretKey.Length is 0 or > 255))
            throw new ValidationException("Invalid keys", [new ValidationError("secret_key", "Required")]);
        return new ProviderKeyInfo(false);
    }

    public Task<CheckoutSession> CreateCheckoutAsync(PaymentCredentials credentials, CheckoutRequest request, CancellationToken ct = default)
    {
        lock (Checkouts) Checkouts.Add(request);
        if (Fail) throw new PaymentProviderException("The payment provider rejected the request");
        var sessionId = $"fake_cs_{request.DonationId:N}";
        return Task.FromResult(new CheckoutSession(sessionId, $"https://fake-checkout.invalid/pay/{sessionId}"));
    }

    public Task<RefundResult> RefundAsync(PaymentCredentials credentials, RefundRequest request, CancellationToken ct = default)
    {
        lock (Refunds) Refunds.Add(request);
        if (Fail) throw new PaymentProviderException("The payment provider did not accept the refund");
        return Task.FromResult(new RefundResult($"fake_re_{Guid.NewGuid():N}", "succeeded"));
    }

    public static string Sign(string webhookSecret, string rawBody)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

    public ProviderWebhookEvent ParseWebhook(PaymentCredentials credentials, string rawBody, IHeaderDictionary headers)
    {
        if (string.IsNullOrEmpty(credentials.WebhookSecret)) throw new WebhookSignatureException("No webhook secret configured");
        var given = headers[SignatureHeader].ToString().ToLowerInvariant();
        var expected = Sign(credentials.WebhookSecret, rawBody);
        if (given.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(expected)))
            throw new WebhookSignatureException("Signature mismatch");

        return StripePaymentProvider.ParseEventBody(rawBody); // same event shape as Stripe
    }
}
