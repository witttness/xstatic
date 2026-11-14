using System.Reflection;
using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Donations;
using Extatic.Api.Features.Donations.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Donations;

public class DonationWebhookTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly DonationTestKit kit = new(fixture);

    private async Task<(App App, Donation Donation)> SeedPendingAsync(
        bool subscribeWebhook = true, string[]? notify = null)
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id, DonationTestKit.CampaignRequest(notify: notify));
        if (subscribeWebhook)
        {
            await using var db = fixture.CreateContext();
            db.Webhooks.Add(new Webhook
            {
                AppId = app.Id, Url = "https://hooks.example.org/x", Secret = "s",
                Events = [DonationNotifier.EventSucceeded, DonationNotifier.EventFailed, DonationNotifier.EventRefunded]
            });
            await db.SaveChangesAsync();
        }
        var checkout = await kit.CheckoutAsync(app.Id);
        return (app, await kit.LoadDonationAsync(checkout.DonationId));
    }

    private async Task<(int Receipts, int Notifications, List<string> Webhooks)> StagedAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        var receipts = await db.EmailOutbox.CountAsync(m => m.AppId == appId && m.Category == DonationNotifier.ReceiptCategory);
        var notifications = await db.EmailOutbox.CountAsync(m => m.AppId == appId && m.Category == DonationNotifier.NotificationCategory);
        var hooks = await db.WebhookDeliveryLogs.Where(l => l.Webhook.AppId == appId).Select(l => l.EventType).ToListAsync();
        return (receipts, notifications, hooks);
    }

    [Fact]
    public async Task Completed_MarksSucceeded_AndStagesReceiptNotificationAndWebhook()
    {
        var (app, donation) = await SeedPendingAsync();
        var outcome = await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000));

        Assert.Equal(PaymentWebhookOutcome.Processed, outcome);
        var saved = await kit.LoadDonationAsync(donation.Id);
        Assert.Equal(DonationStatus.Succeeded, saved.Status);
        Assert.Equal("pi_FAKE_1", saved.ProviderPaymentId);
        Assert.NotNull(saved.CompletedAt);

        var (receipts, notifications, hooks) = await StagedAsync(app.Id);
        Assert.Equal(1, receipts);
        Assert.Equal(1, notifications);
        Assert.Equal([DonationNotifier.EventSucceeded], hooks);

        await using var db = fixture.CreateContext();
        var receipt = await db.EmailOutbox.SingleAsync(m => m.AppId == app.Id && m.Category == DonationNotifier.ReceiptCategory);
        Assert.Equal("donor@example.com", receipt.ToAddress);
        Assert.Contains("10.00 USD", receipt.TextBody);
    }

    [Fact]
    public async Task HtmlEmails_EncodeDonorContent()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        var checkout = await kit.CheckoutAsync(app.Id,
            DonationTestKit.CheckoutRequest(name: "<script>x</script>", message: "<img src=x onerror=alert(1)>"));
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", checkout.DonationId, 1000));

        await using var db = fixture.CreateContext();
        var emails = await db.EmailOutbox.Where(m => m.AppId == app.Id).ToListAsync();
        Assert.NotEmpty(emails);
        Assert.All(emails, m =>
        {
            Assert.DoesNotContain("<script>", m.HtmlBody);
            Assert.DoesNotContain("<img", m.HtmlBody);
        });
    }

    [Fact]
    public async Task SameEvent_DeliveredTwice_ProcessedOnce()
    {
        var (app, donation) = await SeedPendingAsync();
        var body = DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, eventId: "evt_dup_seq");
        Assert.Equal(PaymentWebhookOutcome.Processed, await kit.DeliverAsync(app.Id, body));
        Assert.Equal(PaymentWebhookOutcome.Duplicate, await kit.DeliverAsync(app.Id, body));
        var (receipts, _, hooks) = await StagedAsync(app.Id);
        Assert.Equal(1, receipts);
        Assert.Single(hooks);
    }

    [Fact]
    public async Task ParallelDuplicateDeliveries_ProcessExactlyOnce()
    {
        var (app, donation) = await SeedPendingAsync();
        var body = DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, eventId: "evt_dup_parallel");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => kit.DeliverAsync(app.Id, body))));

        Assert.Single(outcomes, o => o == PaymentWebhookOutcome.Processed);
        Assert.All(outcomes.Where(o => o != PaymentWebhookOutcome.Processed), o => Assert.Equal(PaymentWebhookOutcome.Duplicate, o));
        var (receipts, notifications, hooks) = await StagedAsync(app.Id);
        Assert.Equal(1, receipts);
        Assert.Equal(1, notifications);
        Assert.Single(hooks);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.PaymentWebhookEvents.CountAsync(e => e.AppId == app.Id));
    }

    [Fact]
    public async Task DifferentEventsForSameSession_InParallel_SucceedOnce()
    {
        var (app, donation) = await SeedPendingAsync();
        var bodies = Enumerable.Range(0, 6)
            .Select(i => DonationTestKit.SessionEvent(i % 2 == 0 ? "checkout.session.completed" : "checkout.session.async_payment_succeeded",
                donation.Id, 1000))
            .ToList();
        await Task.WhenAll(bodies.Select(b => Task.Run(() => kit.DeliverAsync(app.Id, b))));
        var (receipts, _, hooks) = await StagedAsync(app.Id);
        Assert.Equal(1, receipts);
        Assert.Single(hooks);
    }

    [Fact]
    public async Task CrossTenantEvent_CannotTouchAnotherTenantsDonation()
    {
        var (victim, donation) = await SeedPendingAsync();
        var attacker = await kit.SeedAppAsync();
        const string attackerSecret = "whsec_FAKE_attacker";
        await kit.ConfigureProviderAsync(attacker.Id, webhookSecret: attackerSecret);

        // Attacker signs a valid event (with their own secret) that references the victim's donation.
        var body = DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000);
        Assert.Equal(PaymentWebhookOutcome.Ignored, await kit.DeliverAsync(attacker.Id, body, attackerSecret));
        // Same event at the victim's URL fails the victim's signature check.
        await Assert.ThrowsAsync<WebhookSignatureException>(() => kit.DeliverAsync(victim.Id, body, attackerSecret));

        Assert.Equal(DonationStatus.Pending, (await kit.LoadDonationAsync(donation.Id)).Status);
        Assert.Equal(0, (await StagedAsync(victim.Id)).Receipts);
    }

    [Fact]
    public async Task SessionIdMismatch_Ignored()
    {
        var (app, donation) = await SeedPendingAsync();
        var outcome = await kit.DeliverAsync(app.Id,
            DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, sessionId: "cs_other"));
        Assert.Equal(PaymentWebhookOutcome.Ignored, outcome);
        Assert.Equal(DonationStatus.Pending, (await kit.LoadDonationAsync(donation.Id)).Status);
    }

    [Theory]
    [InlineData(999, "usd")]
    [InlineData(1000, "eur")]
    public async Task AmountOrCurrencyMismatch_Rejected_NoReceipt(long amount, string currency)
    {
        var (app, donation) = await SeedPendingAsync();
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, amount, currency));

        Assert.Equal(DonationStatus.Failed, (await kit.LoadDonationAsync(donation.Id)).Status);
        var (receipts, notifications, hooks) = await StagedAsync(app.Id);
        Assert.Equal(0, receipts);
        Assert.Equal(0, notifications);
        Assert.Empty(hooks);
    }

    [Fact]
    public async Task AsyncPayment_PendingThenSucceeded()
    {
        var (app, donation) = await SeedPendingAsync();
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, paymentStatus: "unpaid"));
        Assert.Equal(DonationStatus.Pending, (await kit.LoadDonationAsync(donation.Id)).Status);
        Assert.Equal(0, (await StagedAsync(app.Id)).Receipts);

        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.async_payment_succeeded", donation.Id, 1000));
        Assert.Equal(DonationStatus.Succeeded, (await kit.LoadDonationAsync(donation.Id)).Status);
        Assert.Equal(1, (await StagedAsync(app.Id)).Receipts);
    }

    [Fact]
    public async Task AsyncPaymentFailed_StagesFailedWebhook()
    {
        var (app, donation) = await SeedPendingAsync();
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.async_payment_failed", donation.Id, 1000));
        Assert.Equal(DonationStatus.Failed, (await kit.LoadDonationAsync(donation.Id)).Status);
        var (receipts, _, hooks) = await StagedAsync(app.Id);
        Assert.Equal(0, receipts);
        Assert.Equal([DonationNotifier.EventFailed], hooks);
    }

    [Fact]
    public async Task Expired_MarksExpired_AndLaterCompletionIgnored()
    {
        var (app, donation) = await SeedPendingAsync();
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.expired", donation.Id, 1000, paymentStatus: "unpaid"));
        Assert.Equal(DonationStatus.Expired, (await kit.LoadDonationAsync(donation.Id)).Status);
        Assert.Equal(PaymentWebhookOutcome.Ignored,
            await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000)));
    }

    [Fact]
    public async Task ChargeRefunded_UpdatesRefundedAmount_Idempotently()
    {
        var (app, donation) = await SeedPendingAsync();
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, paymentIntent: "pi_ref"));

        await kit.DeliverAsync(app.Id, DonationTestKit.RefundEvent("pi_ref", 1000, 400));
        var partial = await kit.LoadDonationAsync(donation.Id);
        Assert.Equal(400, partial.RefundedAmountMinor);
        Assert.Equal(DonationStatus.Succeeded, partial.Status);

        // A different event carrying the same cumulative amount changes nothing.
        Assert.Equal(PaymentWebhookOutcome.Ignored, await kit.DeliverAsync(app.Id, DonationTestKit.RefundEvent("pi_ref", 1000, 400)));

        await kit.DeliverAsync(app.Id, DonationTestKit.RefundEvent("pi_ref", 1000, 1000));
        var full = await kit.LoadDonationAsync(donation.Id);
        Assert.Equal(1000, full.RefundedAmountMinor);
        Assert.Equal(DonationStatus.Refunded, full.Status);
        var hooks = (await StagedAsync(app.Id)).Webhooks;
        Assert.Equal(2, hooks.Count(h => h == DonationNotifier.EventRefunded));
    }

    [Fact]
    public async Task ReceiptAndWebhook_StagedAtomically_WithDonationUpdate()
    {
        var (app, donation) = await SeedPendingAsync();
        // Corrupt a notify address directly in the DB so staging the notification throws mid-way.
        await using (var db = fixture.CreateContext())
            await db.DonationCampaigns.Where(c => c.AppId == app.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.NotifyEmails, new[] { "broken address" }));

        var body = DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000, eventId: "evt_atomic");
        await Assert.ThrowsAnyAsync<Exception>(() => kit.DeliverAsync(app.Id, body));

        // Nothing committed: donation still pending, no receipt, no webhook, event not marked processed.
        Assert.Equal(DonationStatus.Pending, (await kit.LoadDonationAsync(donation.Id)).Status);
        var (receipts, notifications, hooks) = await StagedAsync(app.Id);
        Assert.Equal(0, receipts + notifications);
        Assert.Empty(hooks);
        await using (var db = fixture.CreateContext())
            Assert.False(await db.PaymentWebhookEvents.AnyAsync(e => e.AppId == app.Id));

        // Once fixed, the provider's retry of the same event succeeds.
        await using (var db = fixture.CreateContext())
            await db.DonationCampaigns.Where(c => c.AppId == app.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.NotifyEmails, new[] { "staff@example.org" }));
        Assert.Equal(PaymentWebhookOutcome.Processed, await kit.DeliverAsync(app.Id, body));
        Assert.Equal(DonationStatus.Succeeded, (await kit.LoadDonationAsync(donation.Id)).Status);
    }

    // ---- Controller -----------------------------------------------------------------------------

    private async Task<IActionResult> PostAsync(App app, string body, string secret = DonationTestKit.FakeWebhookSecret,
        string provider = FakePaymentProvider.ProviderName)
    {
        await using var db = fixture.CreateContext();
        var controller = kit.ClientController(db, app);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        foreach (var (k, v) in DonationTestKit.FakeHeaders(body, secret)) controller.Request.Headers[k] = v;
        return await controller.Webhook(provider, CancellationToken.None);
    }

    [Fact]
    public async Task Controller_ReadsRawBody_ValidSignature200_BadSignature400_Unknown200()
    {
        var (app, donation) = await SeedPendingAsync();
        // Whitespace/ordering preserved exactly: the signature is over these bytes.
        var body = "{ \"id\": \"evt_ctrl\",  \"type\": \"checkout.session.completed\", \"data\": " +
                   DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000).Split("\"data\":", 2)[1];
        Assert.IsType<OkObjectResult>(await PostAsync(app, body));
        Assert.Equal(DonationStatus.Succeeded, (await kit.LoadDonationAsync(donation.Id)).Status);

        var bad = await PostAsync(app, DonationTestKit.SessionEvent("checkout.session.completed", donation.Id, 1000), "whsec_FAKE_wrong");
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(bad).StatusCode);

        var unknown = """{"id":"evt_unknown","type":"customer.created","data":{"object":{"id":"cus_FAKE"}}}""";
        Assert.IsType<OkObjectResult>(await PostAsync(app, unknown));
    }

    [Fact]
    public async Task Controller_UnconfiguredProvider_400_UnknownProvider_404()
    {
        var app = await kit.SeedAppAsync();
        var body = """{"id":"evt_x","type":"customer.created","data":{"object":{}}}""";
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await PostAsync(app, body)).StatusCode);
        await Assert.ThrowsAsync<Extatic.Api.Services.NotFoundException>(() => PostAsync(app, body, provider: "paypal"));
    }

    [Fact]
    public void WebhookEndpoint_ApiKeySchemeOnly_NoRateLimit_CheckoutAnonymousWrites()
    {
        var webhook = typeof(ClientDonationsController).GetMethod(nameof(ClientDonationsController.Webhook))!;
        var auth = webhook.GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal(AuthSchemes.ApiKey, auth.AuthenticationSchemes);
        Assert.Null(auth.Policy);
        Assert.Null(webhook.GetCustomAttribute<EnableRateLimitingAttribute>());
        Assert.NotNull(webhook.GetCustomAttribute<DisableRateLimitingAttribute>());

        var checkout = typeof(ClientDonationsController).GetMethod(nameof(ClientDonationsController.Checkout))!;
        Assert.Equal(PolicyNames.ClientAnonymousOrAppUser, checkout.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(RateLimitPolicyNames.ClientAnonymousWrites, checkout.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Fact]
    public void Module_DeclaresDonationWebhookEvents()
    {
        Assert.Equal(["donation.succeeded", "donation.failed", "donation.refunded"], new DonationsModule().WebhookEvents);
        Assert.Equal("donations", new DonationsModule().Key);
    }
}
