using System.Net;
using System.Text;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Donations;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Security;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extatic.Api.Tests.Features.Donations;

/// <summary>Wires the Donations services against the fixture DB with the fake provider (no network).</summary>
public sealed class DonationTestKit
{
    // Obviously fake values only.
    public const string FakeSecretKey = "sk_test_FAKE_secret_key_0000";
    public const string FakeWebhookSecret = "whsec_FAKE_webhook_secret_1111";
    public const string FakePublishableKey = "pk_test_FAKE_publishable_2222";

    public static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly ApiTestFixture fixture;

    public DonationTestKit(ApiTestFixture fixture, StubHttpHandler? stripeHandler = null)
    {
        this.fixture = fixture;
        StripeHandler = stripeHandler ?? new StubHttpHandler();
        Stripe = new StripePaymentProvider(new StubHttpClientFactory(StripeHandler), NullLogger<StripePaymentProvider>.Instance);
        Registry = new PaymentProviderRegistry([Stripe, Fake], allowFake: true);
    }

    public FakePaymentProvider Fake { get; } = new();
    public StubHttpHandler StripeHandler { get; }
    public StripePaymentProvider Stripe { get; }
    public PaymentProviderRegistry Registry { get; }
    public ISecretProtector Protector { get; } = TestServices.SecretProtector();

    public PaymentProviderConfigService Configs(AppDbContext db) => new(db, Protector, Registry);
    public DonationCampaignService Campaigns(AppDbContext db) => new(db);
    public DonationNotifier Notifier() => new(TestServices.EmailQueue(), TestServices.WebhookDispatcher(fixture));

    public DonationCheckoutService Checkout(AppDbContext db)
        => new(db, Campaigns(db), Configs(db), Registry, NullLogger<DonationCheckoutService>.Instance);

    public DonationWebhookService Webhooks(AppDbContext db)
        => new(db, Registry, Configs(db), Notifier(), NullLogger<DonationWebhookService>.Instance);

    public DonationAdminService Admin(AppDbContext db) => new(db, Registry, Configs(db), Notifier());

    public ClientDonationsController ClientController(AppDbContext db, App app, Guid? appUserId = null)
        => new(Campaigns(db), Checkout(db), Webhooks(db), NullLogger<ClientDonationsController>.Instance)
        {
            ControllerContext = TestContexts.Client(app, appUserId)
        };

    public PlatformDonationsController PlatformController(AppDbContext db, App app, Guid userId)
    {
        var configuration = TestServices.Configuration(new Dictionary<string, string?>
        {
            ["Donations:PublicApiBaseUrl"] = "https://api.extatic.test"
        });
        return new PlatformDonationsController(Configs(db), Campaigns(db), Admin(db), configuration)
        {
            ControllerContext = TestContexts.Platform(app, userId)
        };
    }

    public async Task<App> SeedAppAsync()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        return app;
    }

    public async Task ConfigureProviderAsync(Guid appId, string provider = FakePaymentProvider.ProviderName,
        string secretKey = FakeSecretKey, string webhookSecret = FakeWebhookSecret)
    {
        await using var db = fixture.CreateContext();
        await Configs(db).PutAsync(appId, provider, new PutProviderConfigRequest(null, secretKey, webhookSecret, true), "https://x.test/hook");
    }

    public static CampaignRequest CampaignRequest(string slug = "general", string currency = "usd",
        long[]? suggested = null, long? min = 100, long? max = 100_000, bool allowCustom = true, bool allowRecurring = true,
        string[]? notify = null, bool showDonors = true, long? goal = 100_000, bool active = true,
        DateTime? startsAt = null, DateTime? endsAt = null)
        => new("General Fund", slug, "Support us", currency, goal, suggested ?? [500, 1000, 2500], min, max,
            allowCustom, allowRecurring, "https://example.org/thanks", "https://example.org/donate", showDonors,
            notify ?? ["staff@example.org"], active, startsAt, endsAt);

    public async Task<CampaignDto> SeedCampaignAsync(Guid appId, CampaignRequest? request = null)
    {
        await using var db = fixture.CreateContext();
        return await Campaigns(db).CreateAsync(appId, request ?? CampaignRequest());
    }

    public static ClientCheckoutRequest CheckoutRequest(long amount = 1000, string? slug = "general",
        string? interval = null, string? email = "donor@example.com", string? name = "Ada", bool anonymous = false,
        string? message = "Keep it up", string? currency = null)
        => new(slug, amount, currency, interval, name, email, anonymous, message);

    public async Task<CheckoutResponse> CheckoutAsync(Guid appId, ClientCheckoutRequest? request = null, Guid? appUserId = null)
    {
        await using var db = fixture.CreateContext();
        return await Checkout(db).CheckoutAsync(appId, appUserId, "iphash", request ?? CheckoutRequest());
    }

    public async Task<Donation> LoadDonationAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Donations, d => d.Id == id);
    }

    // ---- Fake-provider webhooks (same event shape as Stripe) ----------------------------------

    public static string SessionEvent(string type, Guid donationId, long amount, string currency = "usd",
        string paymentStatus = "paid", string? eventId = null, string? sessionId = null, string? paymentIntent = "pi_FAKE_1")
        => JsonSerializer.Serialize(new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            type,
            data = new
            {
                @object = new
                {
                    id = sessionId ?? $"fake_cs_{donationId:N}",
                    client_reference_id = donationId.ToString(),
                    metadata = new { donation_id = donationId.ToString() },
                    amount_total = amount,
                    currency,
                    payment_status = paymentStatus,
                    payment_intent = paymentIntent,
                    subscription = (string?)null
                }
            }
        });

    public static string RefundEvent(string paymentIntent, long amount, long refunded, string currency = "usd", string? eventId = null)
        => JsonSerializer.Serialize(new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            type = "charge.refunded",
            data = new { @object = new { id = "ch_FAKE", payment_intent = paymentIntent, amount, amount_refunded = refunded, currency } }
        });

    public static IHeaderDictionary FakeHeaders(string body, string secret = FakeWebhookSecret)
        => new HeaderDictionary { [FakePaymentProvider.SignatureHeader] = FakePaymentProvider.Sign(secret, body) };

    public async Task<PaymentWebhookOutcome> DeliverAsync(Guid appId, string body, string secret = FakeWebhookSecret)
    {
        await using var db = fixture.CreateContext();
        return await Webhooks(db).HandleAsync(appId, FakePaymentProvider.ProviderName, body, FakeHeaders(body, secret));
    }

    /// <summary>Checkout + completed webhook → a Succeeded donation.</summary>
    public async Task<Donation> SeedSucceededDonationAsync(Guid appId, long amount = 1000, string paymentIntent = "pi_FAKE_1",
        ClientCheckoutRequest? request = null)
    {
        var checkout = await CheckoutAsync(appId, request ?? CheckoutRequest(amount));
        var donation = await LoadDonationAsync(checkout.DonationId);
        var outcome = await DeliverAsync(appId,
            SessionEvent("checkout.session.completed", donation.Id, donation.AmountMinor, donation.Currency, paymentIntent: paymentIntent));
        Assert.Equal(PaymentWebhookOutcome.Processed, outcome);
        return await LoadDonationAsync(donation.Id);
    }

    public static IConfiguration Configuration() => TestServices.Configuration();
}

/// <summary>Records requests and returns a canned response. Never touches the network.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = """{"id":"cs_test_FAKE123","url":"https://checkout.stripe.com/c/pay/cs_test_FAKE123"}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        Assert.Equal(StripePaymentProvider.HttpClientName, name);
        return new HttpClient(handler, disposeHandler: false);
    }
}
