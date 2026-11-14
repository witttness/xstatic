using System.Net;
using System.Web;
using Extatic.Api.Features.Donations;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extatic.Api.Tests.Features.Donations;

public class StripePaymentProviderTests
{
    private const string Secret = DonationTestKit.FakeWebhookSecret;
    private static readonly PaymentCredentials Creds = new(DonationTestKit.FakeSecretKey, null, Secret);

    private static (StripePaymentProvider Provider, StubHttpHandler Handler) Create(DateTimeOffset? now = null)
    {
        var handler = new StubHttpHandler();
        var provider = new StripePaymentProvider(new StubHttpClientFactory(handler), NullLogger<StripePaymentProvider>.Instance)
        {
            UtcNow = () => now ?? DateTimeOffset.UtcNow
        };
        return (provider, handler);
    }

    private static CheckoutRequest Request(DonationInterval interval, long amount = 2500, string currency = "usd") => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        amount, currency, interval, "General Fund", "https://example.org/thanks?donation_id=x", "https://example.org/donate",
        "donor@example.com");

    private static Dictionary<string, string> Form(string body)
    {
        var parsed = HttpUtility.ParseQueryString(body);
        return parsed.AllKeys.ToDictionary(k => k!, k => parsed[k]!);
    }

    [Fact]
    public async Task OneTimeCheckout_SendsExpectedFormAuthAndIdempotencyKey()
    {
        var (provider, handler) = Create();
        var session = await provider.CreateCheckoutAsync(Creds, Request(DonationInterval.OneTime));

        Assert.Equal("cs_test_FAKE123", session.SessionId);
        Assert.StartsWith("https://checkout.stripe.com/", session.CheckoutUrl);
        var (req, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.stripe.com/v1/checkout/sessions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal(DonationTestKit.FakeSecretKey, req.Headers.Authorization.Parameter);
        Assert.Equal("11111111-2222-3333-4444-555555555555", Assert.Single(req.Headers.GetValues("Idempotency-Key")));
        Assert.Equal("application/x-www-form-urlencoded", req.Content!.Headers.ContentType!.MediaType);

        var form = Form(body);
        Assert.Equal("payment", form["mode"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", form["client_reference_id"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", form["metadata[donation_id]"]);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", form["metadata[app_id]"]);
        Assert.Equal("usd", form["line_items[0][price_data][currency]"]);
        Assert.Equal("2500", form["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("General Fund", form["line_items[0][price_data][product_data][name]"]);
        Assert.Equal("1", form["line_items[0][quantity]"]);
        Assert.Equal("https://example.org/thanks?donation_id=x", form["success_url"]);
        Assert.Equal("https://example.org/donate", form["cancel_url"]);
        Assert.Equal("donor@example.com", form["customer_email"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", form["payment_intent_data[metadata][donation_id]"]);
        Assert.DoesNotContain(form.Keys, k => k.Contains("recurring"));
        Assert.DoesNotContain(form.Keys, k => k.StartsWith("subscription_data"));
    }

    [Theory]
    [InlineData(DonationInterval.Monthly, "month")]
    [InlineData(DonationInterval.Yearly, "year")]
    public async Task RecurringCheckout_UsesSubscriptionModeWithInlineRecurringPrice(DonationInterval interval, string expected)
    {
        var (provider, handler) = Create();
        await provider.CreateCheckoutAsync(Creds, Request(interval));

        var form = Form(Assert.Single(handler.Requests).Body);
        Assert.Equal("subscription", form["mode"]);
        Assert.Equal(expected, form["line_items[0][price_data][recurring][interval]"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", form["subscription_data[metadata][donation_id]"]);
        Assert.DoesNotContain(form.Keys, k => k.StartsWith("payment_intent_data"));
    }

    [Fact]
    public async Task ZeroDecimalCurrency_AmountSentAsIs()
    {
        var (provider, handler) = Create();
        await provider.CreateCheckoutAsync(Creds, Request(DonationInterval.OneTime, amount: 500, currency: "jpy"));
        var form = Form(Assert.Single(handler.Requests).Body);
        Assert.Equal("500", form["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("jpy", form["line_items[0][price_data][currency]"]);
    }

    [Fact]
    public async Task ProviderError_ThrowsSanitisedException_WithoutBodyOrKey()
    {
        var (provider, handler) = Create();
        handler.Status = HttpStatusCode.BadRequest;
        handler.ResponseBody = """{"error":{"message":"Invalid API Key provided: sk_test_FAKE****0000","type":"invalid_request_error"}}""";

        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() => provider.CreateCheckoutAsync(Creds, Request(DonationInterval.OneTime)));
        Assert.DoesNotContain("sk_", ex.Message);
        Assert.DoesNotContain("Invalid API Key", ex.Message);
    }

    [Fact]
    public async Task Refund_SendsPaymentIntentAmountAndIdempotencyKey()
    {
        var (provider, handler) = Create();
        handler.ResponseBody = """{"id":"re_FAKE","status":"succeeded"}""";
        var result = await provider.RefundAsync(Creds, new RefundRequest(Guid.NewGuid(), "pi_FAKE", 300, "refund-key-1"));

        Assert.Equal("re_FAKE", result.RefundId);
        var (req, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.stripe.com/v1/refunds", req.RequestUri!.ToString());
        Assert.Equal("refund-key-1", Assert.Single(req.Headers.GetValues("Idempotency-Key")));
        var form = Form(body);
        Assert.Equal("pi_FAKE", form["payment_intent"]);
        Assert.Equal("300", form["amount"]);
    }

    // ---- Signatures ---------------------------------------------------------------------------

    private const string Body = """{"id":"evt_FAKE1","type":"checkout.session.completed","data":{"object":{"id":"cs_test_FAKE","client_reference_id":"11111111-2222-3333-4444-555555555555","amount_total":2500,"currency":"usd","payment_status":"paid","payment_intent":"pi_FAKE"}}}""";

    private static IHeaderDictionary Signed(string body, string secret, DateTimeOffset at, string extra = "")
    {
        var t = at.ToUnixTimeSeconds().ToString();
        return new HeaderDictionary
        {
            ["Stripe-Signature"] = $"t={t},v1={StripePaymentProvider.ComputeSignature(secret, t, body)}{extra}"
        };
    }

    [Fact]
    public void ValidSignature_ParsesNormalisedEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        var evt = provider.ParseWebhook(Creds, Body, Signed(Body, Secret, now.AddSeconds(-30), ",v0=ignored,payload=abc"));

        Assert.Equal("evt_FAKE1", evt.EventId);
        Assert.Equal(ProviderWebhookEventKind.CheckoutCompleted, evt.Kind);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), evt.DonationId);
        Assert.Equal("cs_test_FAKE", evt.SessionId);
        Assert.Equal(2500, evt.AmountMinor);
        Assert.Equal("usd", evt.Currency);
        Assert.True(evt.IsPaid);
        Assert.Equal("pi_FAKE", evt.PaymentId);
    }

    [Fact]
    public void AnyMatchingV1Signature_IsAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        var t = now.ToUnixTimeSeconds().ToString();
        var headers = new HeaderDictionary
        {
            ["Stripe-Signature"] = $"t={t},v1={new string('0', 64)},v1={StripePaymentProvider.ComputeSignature(Secret, t, Body)}"
        };
        Assert.Equal("evt_FAKE1", provider.ParseWebhook(Creds, Body, headers).EventId);
    }

    [Fact]
    public void TamperedBody_Rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        var headers = Signed(Body, Secret, now);
        Assert.Throws<WebhookSignatureException>(() => provider.ParseWebhook(Creds, Body.Replace("2500", "250000"), headers));
    }

    [Fact]
    public void WrongSecret_Rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        Assert.Throws<WebhookSignatureException>(() =>
            provider.ParseWebhook(Creds, Body, Signed(Body, "whsec_FAKE_other_tenant", now)));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void StaleOrFutureTimestamp_Rejected(int offsetSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        Assert.Throws<WebhookSignatureException>(() =>
            provider.ParseWebhook(Creds, Body, Signed(Body, Secret, now.AddSeconds(offsetSeconds))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=notanumber,v1=abc")]
    [InlineData("t=123")]
    public void MalformedHeader_Rejected(string header)
    {
        var (provider, _) = Create();
        Assert.Throws<WebhookSignatureException>(() =>
            provider.ParseWebhook(Creds, Body, new HeaderDictionary { ["Stripe-Signature"] = header }));
    }

    [Fact]
    public void MissingWebhookSecret_Rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        Assert.Throws<WebhookSignatureException>(() =>
            provider.ParseWebhook(Creds with { WebhookSecret = null }, Body, Signed(Body, Secret, now)));
    }

    [Fact]
    public void UnknownEventType_ParsesAsUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var (provider, _) = Create(now);
        const string body = """{"id":"evt_FAKE2","type":"customer.created","data":{"object":{"id":"cus_FAKE"}}}""";
        Assert.Equal(ProviderWebhookEventKind.Unknown, provider.ParseWebhook(Creds, body, Signed(body, Secret, now)).Kind);
    }

    // ---- Keys -----------------------------------------------------------------------------------

    [Fact]
    public void KeyValidation_DerivesModeAndRejectsMismatches()
    {
        var (provider, _) = Create();
        Assert.False(provider.ValidateKeys("sk_test_FAKE", "pk_test_FAKE", "whsec_FAKE").IsLiveMode);
        Assert.True(provider.ValidateKeys("sk_live_FAKE", "pk_live_FAKE", null).IsLiveMode);
        Assert.True(provider.ValidateKeys("rk_live_FAKE", null, null).IsLiveMode);
        Assert.Throws<ValidationException>(() => provider.ValidateKeys("sk_live_FAKE", "pk_test_FAKE", null));
        Assert.Throws<ValidationException>(() => provider.ValidateKeys("pk_test_FAKE", null, null));
        Assert.Throws<ValidationException>(() => provider.ValidateKeys("sk_test_FAKE", null, "not-a-secret"));
    }
}
