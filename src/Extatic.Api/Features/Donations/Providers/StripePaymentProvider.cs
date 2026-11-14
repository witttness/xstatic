using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Extatic.Api.Services;

namespace Extatic.Api.Features.Donations.Providers;

/// <summary>
/// Stripe Checkout over raw HTTPS (no Stripe SDK). Uses the named HttpClient <see cref="HttpClientName"/>.
/// Error responses are never surfaced or logged beyond the HTTP status code.
/// </summary>
public class StripePaymentProvider(IHttpClientFactory httpClientFactory, ILogger<StripePaymentProvider> logger)
    : IPaymentProvider
{
    public const string ProviderName = "stripe";
    public const string HttpClientName = "donations.stripe";
    public static readonly Uri ApiBase = new("https://api.stripe.com/");
    public static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Overridable clock for signature-timestamp tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; init; } = () => DateTimeOffset.UtcNow;

    public string Name => ProviderName;

    public ProviderKeyInfo ValidateKeys(string? secretKey, string? publishableKey, string? webhookSecret)
    {
        var errors = new List<ValidationError>();
        bool? secretLive = null;
        if (secretKey is not null)
        {
            secretLive = secretKey.StartsWith("sk_live_") || secretKey.StartsWith("rk_live_") ? true
                : secretKey.StartsWith("sk_test_") || secretKey.StartsWith("rk_test_") ? false
                : null;
            if (secretLive is null || secretKey.Length > 255 || secretKey.Any(char.IsWhiteSpace))
                errors.Add(new ValidationError("secret_key", "Must be a Stripe secret or restricted key (sk_test_…, sk_live_…, rk_test_…, rk_live_…)"));
        }
        if (publishableKey is not null)
        {
            bool? pkLive = publishableKey.StartsWith("pk_live_") ? true : publishableKey.StartsWith("pk_test_") ? false : null;
            if (pkLive is null || publishableKey.Length > 255 || publishableKey.Any(char.IsWhiteSpace))
                errors.Add(new ValidationError("publishable_key", "Must be a Stripe publishable key (pk_test_… or pk_live_…)"));
            else if (secretLive is not null && pkLive != secretLive)
                errors.Add(new ValidationError("publishable_key", "Publishable key and secret key must both be test or both be live"));
        }
        if (webhookSecret is not null && (!webhookSecret.StartsWith("whsec_") || webhookSecret.Length > 255 || webhookSecret.Any(char.IsWhiteSpace)))
            errors.Add(new ValidationError("webhook_secret", "Must be a Stripe webhook signing secret (whsec_…)"));
        if (errors.Count > 0) throw new ValidationException("Invalid Stripe keys", errors);
        return new ProviderKeyInfo(secretLive ?? false);
    }

    public async Task<CheckoutSession> CreateCheckoutAsync(PaymentCredentials credentials, CheckoutRequest request,
        CancellationToken ct = default)
    {
        var form = BuildCheckoutForm(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBase, "v1/checkout/sessions"))
        {
            Content = new FormUrlEncodedContent(form)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.SecretKey);
        message.Headers.Add("Idempotency-Key", request.DonationId.ToString());

        using var doc = await SendAsync(message, "checkout session", ct);
        var root = doc.RootElement;
        var id = GetString(root, "id");
        var url = GetString(root, "url");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            throw new PaymentProviderException("The payment provider returned an unexpected response");
        return new CheckoutSession(id, url);
    }

    /// <summary>The exact form fields sent to POST /v1/checkout/sessions.</summary>
    public static List<KeyValuePair<string, string>> BuildCheckoutForm(CheckoutRequest request)
    {
        var amount = request.AmountMinor.ToString(CultureInfo.InvariantCulture);
        var donationId = request.DonationId.ToString();
        var appId = request.AppId.ToString();
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", request.Interval == DonationInterval.OneTime ? "payment" : "subscription"),
            new("success_url", request.SuccessUrl),
            new("cancel_url", request.CancelUrl),
            new("client_reference_id", donationId),
            new("metadata[donation_id]", donationId),
            new("metadata[app_id]", appId),
            new("line_items[0][quantity]", "1"),
            new("line_items[0][price_data][currency]", request.Currency),
            new("line_items[0][price_data][unit_amount]", amount),
            new("line_items[0][price_data][product_data][name]", request.ProductName),
        };
        if (request.Interval == DonationInterval.OneTime)
        {
            form.Add(new("payment_intent_data[metadata][donation_id]", donationId));
            form.Add(new("payment_intent_data[metadata][app_id]", appId));
        }
        else
        {
            form.Add(new("line_items[0][price_data][recurring][interval]",
                request.Interval == DonationInterval.Monthly ? "month" : "year"));
            form.Add(new("subscription_data[metadata][donation_id]", donationId));
            form.Add(new("subscription_data[metadata][app_id]", appId));
        }
        if (!string.IsNullOrEmpty(request.CustomerEmail))
            form.Add(new("customer_email", request.CustomerEmail));
        return form;
    }

    public async Task<RefundResult> RefundAsync(PaymentCredentials credentials, RefundRequest request,
        CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBase, "v1/refunds"))
        {
            Content = new FormUrlEncodedContent(
            [
                new("payment_intent", request.PaymentId),
                new("amount", request.AmountMinor.ToString(CultureInfo.InvariantCulture)),
                new("metadata[donation_id]", request.DonationId.ToString()),
            ])
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.SecretKey);
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey);

        using var doc = await SendAsync(message, "refund", ct);
        var id = GetString(doc.RootElement, "id");
        var status = GetString(doc.RootElement, "status") ?? "unknown";
        if (string.IsNullOrEmpty(id) || status is "failed" or "canceled")
            throw new PaymentProviderException("The payment provider did not accept the refund");
        return new RefundResult(id, status);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, string operation, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Stripe {Operation} request failed: {ErrorType}", operation, ex.GetType().Name);
            throw new PaymentProviderException("The payment provider could not be reached");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Never log or return the body: it can echo request parameters.
                logger.LogWarning("Stripe {Operation} request returned HTTP {StatusCode}", operation, (int)response.StatusCode);
                throw new PaymentProviderException("The payment provider rejected the request");
            }
            try
            {
                var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new PaymentProviderException("The payment provider returned an unexpected response");
            }
        }
    }

    public ProviderWebhookEvent ParseWebhook(PaymentCredentials credentials, string rawBody, IHeaderDictionary headers)
    {
        if (string.IsNullOrEmpty(credentials.WebhookSecret))
            throw new WebhookSignatureException("No webhook secret configured");
        VerifySignature(rawBody, headers["Stripe-Signature"].ToString(), credentials.WebhookSecret, UtcNow());
        return ParseEventBody(rawBody);
    }

    /// <summary>Normalises an (already verified) Stripe event JSON body.</summary>
    public static ProviderWebhookEvent ParseEventBody(string rawBody)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawBody); }
        catch (JsonException) { throw new WebhookSignatureException("Malformed event body"); }
        using (doc)
        {
            var root = doc.RootElement;
            var eventId = GetString(root, "id");
            var type = GetString(root, "type");
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(type))
                throw new WebhookSignatureException("Malformed event body");

            var kind = type switch
            {
                "checkout.session.completed" => ProviderWebhookEventKind.CheckoutCompleted,
                "checkout.session.async_payment_succeeded" => ProviderWebhookEventKind.AsyncPaymentSucceeded,
                "checkout.session.async_payment_failed" => ProviderWebhookEventKind.AsyncPaymentFailed,
                "checkout.session.expired" => ProviderWebhookEventKind.CheckoutExpired,
                "charge.refunded" => ProviderWebhookEventKind.ChargeRefunded,
                _ => ProviderWebhookEventKind.Unknown
            };
            var evt = new ProviderWebhookEvent(eventId, type, kind);
            if (kind == ProviderWebhookEventKind.Unknown
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("object", out var obj) || obj.ValueKind != JsonValueKind.Object)
                return evt with { Kind = ProviderWebhookEventKind.Unknown };

            if (kind == ProviderWebhookEventKind.ChargeRefunded)
            {
                return evt with
                {
                    PaymentId = GetString(obj, "payment_intent"),
                    AmountMinor = GetLong(obj, "amount"),
                    Currency = GetString(obj, "currency")?.ToLowerInvariant(),
                    RefundedAmountMinor = GetLong(obj, "amount_refunded"),
                };
            }

            Guid? donationId = Guid.TryParse(GetString(obj, "client_reference_id"), out var refId) ? refId
                : obj.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
                  && Guid.TryParse(GetString(meta, "donation_id"), out var metaId) ? metaId : null;
            var paymentStatus = GetString(obj, "payment_status");
            return evt with
            {
                SessionId = GetString(obj, "id"),
                DonationId = donationId,
                AmountMinor = GetLong(obj, "amount_total"),
                Currency = GetString(obj, "currency")?.ToLowerInvariant(),
                IsPaid = paymentStatus is "paid" or "no_payment_required",
                PaymentId = GetString(obj, "payment_intent"),
                SubscriptionId = GetString(obj, "subscription"),
            };
        }
    }

    /// <summary>
    /// Stripe-Signature: <c>t=&lt;unix&gt;,v1=&lt;hex&gt;[,v1=…][,v0=…][,other=…]</c>. Expected v1 =
    /// hex(HMAC-SHA256(key = UTF-8 webhook secret, msg = "{t}.{rawBody}")). Any matching v1 passes
    /// (Stripe sends several while a secret is being rolled). Constant-time compare; 5-minute tolerance.
    /// </summary>
    public static void VerifySignature(string rawBody, string? header, string webhookSecret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header)) throw new WebhookSignatureException("Missing signature");
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (key == "t") timestamp = value;
            else if (key == "v1") signatures.Add(value);
        }
        if (timestamp is null || signatures.Count == 0
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
            throw new WebhookSignatureException("Malformed signature header");

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(unix, 0, 253402300799));
        if ((now - signedAt).Duration() > SignatureTolerance)
            throw new WebhookSignatureException("Signature timestamp outside tolerance");

        var expected = ComputeSignature(webhookSecret, timestamp, rawBody);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var ok = false;
        foreach (var candidate in signatures)
        {
            var candidateBytes = Encoding.ASCII.GetBytes(candidate.ToLowerInvariant());
            if (candidateBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
                ok = true;
        }
        if (!ok) throw new WebhookSignatureException("Signature mismatch");
    }

    public static string ComputeSignature(string webhookSecret, string timestamp, string rawBody)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"))).ToLowerInvariant();

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? GetLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;
}
