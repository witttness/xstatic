using System.Text;
using System.Text.Json;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Features.Donations;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Donations;

public class DonationPlatformTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly DonationTestKit kit = new(fixture);

    private static string Json(IActionResult result)
        => JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(result).Value, DonationTestKit.ApiJson);

    private static string Json<T>(ActionResult<T> result)
        => JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(result.Result).Value, DonationTestKit.ApiJson);

    // ---- Provider config / secrets -------------------------------------------------------------

    [Fact]
    public async Task Secrets_StoredProtected_AndNeverInAnyApiResponse()
    {
        var app = await kit.SeedAppAsync();
        const string secret = "sk_test_FAKE_TOPSECRET_9876";
        const string hook = "whsec_FAKE_TOPSECRET_5432";
        await using var db = fixture.CreateContext();
        var controller = kit.PlatformController(db, app, app.OwnerId);

        var responses = new List<string>
        {
            Json(await controller.PutProvider("stripe", new PutProviderConfigRequest("pk_test_FAKE_pub", secret, hook, true))),
            Json(await controller.GetProvider("stripe")),
            // Update without secrets keeps them.
            Json(await controller.PutProvider("stripe", new PutProviderConfigRequest(null, null, null, false))),
        };
        foreach (var json in responses)
        {
            Assert.DoesNotContain("TOPSECRET", json);
            Assert.DoesNotContain("protected", json);
        }
        var dto = JsonDocument.Parse(responses[1]).RootElement;
        Assert.True(dto.GetProperty("has_secret_key").GetBoolean());
        Assert.True(dto.GetProperty("has_webhook_secret").GetBoolean());
        Assert.Equal("9876", dto.GetProperty("secret_key_last4").GetString());
        Assert.Equal("5432", dto.GetProperty("webhook_secret_last4").GetString());
        Assert.False(dto.GetProperty("is_live_mode").GetBoolean());
        Assert.Equal($"https://api.extatic.test/api/{app.PublicId}/donations/webhooks/stripe", dto.GetProperty("webhook_url").GetString());

        await using var verify = fixture.CreateContext();
        var row = await verify.PaymentProviderConfigs.SingleAsync(c => c.AppId == app.Id);
        Assert.DoesNotContain("TOPSECRET", row.SecretKeyProtected);
        Assert.DoesNotContain("TOPSECRET", row.WebhookSecretProtected);
        Assert.Equal(secret, kit.Protector.Unprotect(row.SecretKeyProtected));
        Assert.Equal(hook, kit.Protector.Unprotect(row.WebhookSecretProtected!));
        Assert.False(row.IsEnabled);
    }

    [Fact]
    public async Task ProviderConfig_Validation()
    {
        var app = await kit.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var configs = kit.Configs(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            configs.PutAsync(app.Id, "stripe", new PutProviderConfigRequest(null, null, null, true), "u")); // secret required
        await Assert.ThrowsAsync<ValidationException>(() =>
            configs.PutAsync(app.Id, "stripe", new PutProviderConfigRequest("pk_live_FAKE", "sk_test_FAKE", null, true), "u"));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            configs.PutAsync(app.Id, "paypal", new PutProviderConfigRequest(null, "x", null, true), "u"));

        var live = await configs.PutAsync(app.Id, "stripe", new PutProviderConfigRequest(null, "sk_live_FAKE", null, true), "u");
        Assert.True(live.IsLiveMode);
        // Later publishable key is checked against the stored secret's mode.
        await Assert.ThrowsAsync<ValidationException>(() =>
            configs.PutAsync(app.Id, "stripe", new PutProviderConfigRequest("pk_test_FAKE", null, null, null), "u"));
    }

    [Fact]
    public async Task ProviderConfig_ConcurrentCreates_OneRow()
    {
        var app = await kit.SeedAppAsync();
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            try
            {
                await kit.Configs(db).PutAsync(app.Id, "stripe", new PutProviderConfigRequest(null, "sk_test_FAKE", null, true), "u");
                return true;
            }
            catch (ConflictException) { return false; }
        }));
        var results = await Task.WhenAll(tasks);
        Assert.Contains(true, results);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.PaymentProviderConfigs.CountAsync(c => c.AppId == app.Id));
    }

    [Fact]
    public async Task DeleteProvider_RemovesConfig()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id, "stripe");
        await using var db = fixture.CreateContext();
        Assert.IsType<NoContentResult>(await kit.PlatformController(db, app, app.OwnerId).DeleteProvider("stripe"));
        await Assert.ThrowsAsync<NotFoundException>(() => kit.Configs(db).DeleteAsync(app.Id, "stripe"));
        Assert.False((await kit.Configs(db).GetAsync(app.Id, "stripe", "u")).Configured);
    }

    // ---- Campaigns ------------------------------------------------------------------------------

    [Theory]
    [InlineData("http://example.org/thanks")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative")]
    [InlineData("https://user:pw@example.org/")]
    public void Campaign_RejectsUnsafeRedirectUrls(string url)
    {
        var req = DonationTestKit.CampaignRequest() with { SuccessUrl = url };
        var ex = Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(new DonationCampaign(), req));
        Assert.Contains(ex.Errors, e => e.Pointer == "success_url");
    }

    [Fact]
    public void Campaign_AcceptsLocalhostHttp_AndValidatesAmountsAndCurrency()
    {
        var c = new DonationCampaign();
        DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest() with { SuccessUrl = "http://localhost:4321/thanks" });
        Assert.Equal("http://localhost:4321/thanks", c.SuccessUrl);

        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(currency: "xyz")));
        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(min: 500, max: 100)));
        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(suggested: [50])));
        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(max: 100_000_000)));
        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(notify: ["nope"])));
        Assert.Throws<ValidationException>(() => DonationCampaignService.Apply(c, DonationTestKit.CampaignRequest(allowCustom: false, suggested: [])));
    }

    [Fact]
    public async Task Campaign_ConcurrentSameSlug_OneWins()
    {
        var app = await kit.SeedAppAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await kit.SeedCampaignAsync(app.Id, DonationTestKit.CampaignRequest("same")); return true; }
            catch (ConflictException) { return false; }
        })));
        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task Campaign_Delete_KeepsDonationsWithNullCampaign()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        var campaign = await kit.SeedCampaignAsync(app.Id);
        var donation = await kit.SeedSucceededDonationAsync(app.Id);
        await using var db = fixture.CreateContext();
        await kit.Campaigns(db).DeleteAsync(app.Id, campaign.Id);
        Assert.Null((await kit.LoadDonationAsync(donation.Id)).CampaignId);
    }

    // ---- Totals ---------------------------------------------------------------------------------

    [Fact]
    public async Task Totals_CountSucceededMinusRefunded()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        var campaign = await kit.SeedCampaignAsync(app.Id);
        var a = await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_a");
        var b = await kit.SeedSucceededDonationAsync(app.Id, 2500, "pi_b");
        var c = await kit.SeedSucceededDonationAsync(app.Id, 500, "pi_c");
        await kit.CheckoutAsync(app.Id, DonationTestKit.CheckoutRequest(5000)); // pending: not counted
        var failed = await kit.CheckoutAsync(app.Id, DonationTestKit.CheckoutRequest(7000));
        await kit.DeliverAsync(app.Id, DonationTestKit.SessionEvent("checkout.session.async_payment_failed", failed.DonationId, 7000));

        await using (var db = fixture.CreateContext())
        {
            await kit.Admin(db).RefundAsync(app.Id, b.Id, 1000); // partial
            await kit.Admin(db).RefundAsync(app.Id, c.Id, null); // full
        }

        await using var verify = fixture.CreateContext();
        var dto = await kit.Campaigns(verify).GetDtoAsync(app.Id, campaign.Id);
        Assert.Equal(1000 + 1500, dto.RaisedMinor);
        Assert.Equal(2, dto.DonorCount);

        var client = await kit.Campaigns(verify).GetOpenDtoAsync(app.Id, "general", DateTime.UtcNow);
        Assert.Equal(2500, client.RaisedMinor);
        Assert.Equal(100_000, client.GoalMinor);
        Assert.Equal(2, client.RecentDonors!.Count);
        Assert.Contains(client.RecentDonors, d => d.AmountMinor == 1500);

        var summary = await kit.Admin(verify).SummaryAsync(app.Id);
        var usd = Assert.Single(summary.TotalRaised);
        Assert.Equal(("usd", 2500L, 2), (usd.Currency, usd.AmountMinor, usd.DonationCount));
        Assert.Equal(2500, Assert.Single(summary.Last30Days).AmountMinor);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(a.Id, (await kit.Admin(verify).GetAsync(app.Id, a.Id)).Id);
    }

    [Fact]
    public async Task ClientCampaign_DonorList_NeverExposesEmail_AndHonoursAnonymous()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_1",
            DonationTestKit.CheckoutRequest(1000, name: "Secret Person", email: "hidden@example.com", anonymous: true));
        await kit.SeedSucceededDonationAsync(app.Id, 2000, "pi_2",
            DonationTestKit.CheckoutRequest(2000, name: "Grace", email: "grace@example.com"));

        await using var db = fixture.CreateContext();
        var list = Json(await kit.ClientController(db, app).ListCampaigns());
        Assert.DoesNotContain("@example.com", list);
        Assert.DoesNotContain("Secret Person", list);
        Assert.Contains("Anonymous", list);
        Assert.Contains("Grace", list);

        // Donor list hidden when the campaign disables it.
        await db.DonationCampaigns.Where(c => c.AppId == app.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ShowDonorList, false));
        var single = await kit.Campaigns(db).GetOpenDtoAsync(app.Id, "general", DateTime.UtcNow);
        Assert.Null(single.RecentDonors);
    }

    // ---- Refunds --------------------------------------------------------------------------------

    [Fact]
    public async Task Refund_FullAndPartial_WithValidation()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        var donation = await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_r");
        await using var db = fixture.CreateContext();
        var admin = kit.Admin(db);

        await Assert.ThrowsAsync<ValidationException>(() => admin.RefundAsync(app.Id, donation.Id, 1001));
        await Assert.ThrowsAsync<ValidationException>(() => admin.RefundAsync(app.Id, donation.Id, 0));

        var partial = await admin.RefundAsync(app.Id, donation.Id, 300);
        Assert.Equal(300, partial.RefundedAmountMinor);
        Assert.Equal("succeeded", partial.Status);
        var full = await admin.RefundAsync(app.Id, donation.Id, null);
        Assert.Equal(1000, full.RefundedAmountMinor);
        Assert.Equal("refunded", full.Status);
        await Assert.ThrowsAsync<ConflictException>(() => admin.RefundAsync(app.Id, donation.Id, null));

        Assert.Equal([300L, 700L], kit.Fake.Refunds.Select(r => r.AmountMinor));
        Assert.All(kit.Fake.Refunds, r => Assert.Equal("pi_r", r.PaymentId));
        Assert.Equal(2, kit.Fake.Refunds.Select(r => r.IdempotencyKey).Distinct().Count());

        // The provider's charge.refunded echo for the same cumulative amount is a no-op.
        Assert.Equal(PaymentWebhookOutcome.Ignored, await kit.DeliverAsync(app.Id, DonationTestKit.RefundEvent("pi_r", 1000, 1000)));
    }

    [Fact]
    public async Task Refund_PendingDonation_Conflict_AndProviderFailureRollsBack()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        var pending = await kit.CheckoutAsync(app.Id);
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ConflictException>(() => kit.Admin(db).RefundAsync(app.Id, pending.DonationId, null));

        var donation = await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_f");
        kit.Fake.Fail = true;
        var result = await kit.PlatformController(db, app, app.OwnerId).Refund(donation.Id, new RefundDonationRequest(null), CancellationToken.None);
        Assert.Equal(502, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Equal(0, (await kit.LoadDonationAsync(donation.Id)).RefundedAmountMinor);
    }

    [Fact]
    public async Task Refund_ParallelRequests_NeverExceedDonation()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        var donation = await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_par");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            try { await kit.Admin(db).RefundAsync(app.Id, donation.Id, 600); return true; }
            catch (Exception e) when (e is ConflictException or ValidationException) { return false; }
        })));

        Assert.Single(results, r => r);
        Assert.Single(kit.Fake.Refunds);
        Assert.Equal(600, (await kit.LoadDonationAsync(donation.Id)).RefundedAmountMinor);
    }

    // ---- Listing / export / isolation ----------------------------------------------------------

    [Fact]
    public async Task List_FiltersAndSearch_AndExportCsv()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        await kit.SeedSucceededDonationAsync(app.Id, 1000, "pi_l1", DonationTestKit.CheckoutRequest(1000, name: "Ada 100%", email: "ada@example.com"));
        await kit.SeedSucceededDonationAsync(app.Id, 2000, "pi_l2", DonationTestKit.CheckoutRequest(2000, name: "=cmd()", email: "bob@example.com"));
        await kit.CheckoutAsync(app.Id, DonationTestKit.CheckoutRequest(3000, name: "Cy"));

        await using var db = fixture.CreateContext();
        var admin = kit.Admin(db);
        Assert.Equal(3, (await admin.ListAsync(app.Id, new DonationQuery(null, null, null, null, null), 1, 20)).Total);
        Assert.Equal(2, (await admin.ListAsync(app.Id, new DonationQuery("succeeded", null, null, null, null), 1, 20)).Total);
        Assert.Equal(1, (await admin.ListAsync(app.Id, new DonationQuery(null, null, null, null, "BOB@"), 1, 20)).Total);
        Assert.Equal(1, (await admin.ListAsync(app.Id, new DonationQuery(null, null, null, null, "100%"), 1, 20)).Total);
        Assert.Equal(0, (await admin.ListAsync(app.Id, new DonationQuery(null, null, DateTime.UtcNow.AddMinutes(5), null, null), 1, 20)).Total);
        var page = await admin.ListAsync(app.Id, new DonationQuery(null, null, null, null, null), 2, 2);
        Assert.Single(page.Items);
        await Assert.ThrowsAsync<BadRequestException>(() => admin.ListAsync(app.Id, new DonationQuery("bogus", null, null, null, null), 1, 20));

        var csv = Encoding.UTF8.GetString(await admin.ExportCsvAsync(app.Id, new DonationQuery("succeeded", null, null, null, null)));
        Assert.Contains("donor_email", csv);
        Assert.Contains("ada@example.com", csv);
        Assert.Contains("10.00", csv);
        Assert.Contains("'=cmd()", csv); // formula injection defused
        Assert.DoesNotContain("Cy", csv);
    }

    [Fact]
    public async Task PlatformEndpoints_CrossTenant_404()
    {
        var appA = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(appA.Id);
        var campaign = await kit.SeedCampaignAsync(appA.Id);
        var donation = await kit.SeedSucceededDonationAsync(appA.Id, 1000, "pi_x");
        var appB = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(appB.Id);

        await using var db = fixture.CreateContext();
        var asB = kit.PlatformController(db, appB, appB.OwnerId);
        await Assert.ThrowsAsync<NotFoundException>(() => asB.Get(donation.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => asB.Refund(donation.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => asB.GetCampaign(campaign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => asB.UpdateCampaign(campaign.Id, DonationTestKit.CampaignRequest()));
        await Assert.ThrowsAsync<NotFoundException>(() => asB.DeleteCampaign(campaign.Id));
        var list = Assert.IsType<PagedResult<DonationDto>>(Assert.IsType<OkObjectResult>((await asB.List(null, null, null, null, null)).Result).Value);
        Assert.Equal(0, list.Total);
        var campaigns = Assert.IsType<List<CampaignDto>>(Assert.IsType<OkObjectResult>((await asB.ListCampaigns()).Result).Value);
        Assert.Empty(campaigns);
        var summary = Assert.IsType<DonationSummaryDto>(Assert.IsType<OkObjectResult>((await asB.Summary()).Result).Value);
        Assert.Empty(summary.TotalRaised);
        var csv = Assert.IsType<FileContentResult>(await asB.Export(null, null, null, null, null));
        Assert.DoesNotContain("pi_x", Encoding.UTF8.GetString(csv.FileContents));

        Assert.Equal(1000, (await kit.LoadDonationAsync(donation.Id)).AmountMinor);
        Assert.Empty(kit.Fake.Refunds);
    }
}
