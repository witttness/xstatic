using Extatic.Api.Features.Donations;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Donations;

public class DonationCheckoutTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly DonationTestKit kit = new(fixture);

    private async Task<Guid> SeedAsync(CampaignRequest? campaign = null, bool configure = true)
    {
        var app = await kit.SeedAppAsync();
        if (configure) await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id, campaign);
        return app.Id;
    }

    [Fact]
    public async Task Checkout_CreatesPendingDonation_AndReturnsProviderUrl()
    {
        var appId = await SeedAsync();
        var result = await kit.CheckoutAsync(appId);

        Assert.StartsWith("https://fake-checkout.invalid/", result.CheckoutUrl);
        var donation = await kit.LoadDonationAsync(result.DonationId);
        Assert.Equal(DonationStatus.Pending, donation.Status);
        Assert.Equal(appId, donation.AppId);
        Assert.Equal(1000, donation.AmountMinor);
        Assert.Equal("usd", donation.Currency);
        Assert.Equal($"fake_cs_{donation.Id:N}", donation.ProviderSessionId);
        Assert.Equal("iphash", donation.IpHash);

        var sent = kit.Fake.Checkouts.Single(c => c.DonationId == donation.Id);
        Assert.Equal($"https://example.org/thanks?donation_id={donation.Id}", sent.SuccessUrl);
        Assert.Equal("https://example.org/donate", sent.CancelUrl);
    }

    [Theory]
    [InlineData(99)]      // below min (100)
    [InlineData(100_001)] // above max
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Amount_OutsideRange_Rejected(long amount)
    {
        var appId = await SeedAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(amount)));
        Assert.Contains(ex.Errors, e => e.Pointer == "amount_minor");
    }

    [Fact]
    public async Task CustomAmountsOff_OnlySuggestedAccepted()
    {
        var appId = await SeedAsync(DonationTestKit.CampaignRequest(allowCustom: false, suggested: [500, 1000]));
        await Assert.ThrowsAsync<ValidationException>(() => kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(750)));
        var ok = await kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(500));
        Assert.Equal(500, (await kit.LoadDonationAsync(ok.DonationId)).AmountMinor);
    }

    [Fact]
    public async Task CurrencyMismatch_AndDisallowedRecurring_Rejected()
    {
        var appId = await SeedAsync(DonationTestKit.CampaignRequest(allowRecurring: false));
        var currency = await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(currency: "eur")));
        Assert.Contains(currency.Errors, e => e.Pointer == "currency");
        var recurring = await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(interval: "monthly")));
        Assert.Contains(recurring.Errors, e => e.Pointer == "interval");
        await Assert.ThrowsAsync<ValidationException>(() => kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(interval: "weekly")));
    }

    [Fact]
    public async Task ZeroDecimalCurrency_AmountIsMajorUnits()
    {
        var appId = await SeedAsync(DonationTestKit.CampaignRequest(currency: "jpy", min: 100, max: 1_000_000, suggested: [500, 1000]));
        var result = await kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(500, currency: "JPY"));
        var donation = await kit.LoadDonationAsync(result.DonationId);
        Assert.Equal("jpy", donation.Currency);
        Assert.Equal(500, donation.AmountMinor);
        Assert.Equal(500, kit.Fake.Checkouts.Single(c => c.DonationId == donation.Id).AmountMinor);
        Assert.Equal("500 JPY", Currencies.Display(500, "jpy"));
        Assert.Equal("5.00 USD", Currencies.Display(500, "usd"));
    }

    [Fact]
    public async Task Message_TooLong_Rejected_AndControlCharsStripped()
    {
        var appId = await SeedAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(message: new string('x', 501))));
        await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(email: "not-an-email")));
        var ok = await kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(name: "Ada\u0000\r\nLovelace", message: "line1\nline2\u0007"));
        var donation = await kit.LoadDonationAsync(ok.DonationId);
        Assert.Equal("AdaLovelace", donation.DonorName);
        Assert.Equal("line1\nline2", donation.Message);
    }

    [Fact]
    public async Task ProviderFailure_MarksFailed_AndControllerReturns502WithoutLeaking()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        kit.Fake.Fail = true;

        await using var db = fixture.CreateContext();
        var result = await kit.ClientController(db, app).Checkout(DonationTestKit.CheckoutRequest(), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
        Assert.DoesNotContain(DonationTestKit.FakeSecretKey, System.Text.Json.JsonSerializer.Serialize(error.Value));
        await using var verify = fixture.CreateContext();
        var donation = await verify.Donations.SingleAsync(d => d.AppId == app.Id);
        Assert.Equal(DonationStatus.Failed, donation.Status);
        Assert.Null(donation.ProviderSessionId);
    }

    [Fact]
    public async Task NoProviderConfigured_Conflict()
    {
        var appId = await SeedAsync(configure: false);
        await Assert.ThrowsAsync<ConflictException>(() => kit.CheckoutAsync(appId));
        await using var db = fixture.CreateContext();
        Assert.False(await db.Donations.AnyAsync(d => d.AppId == appId));
    }

    [Fact]
    public async Task InactiveOrOutOfWindowCampaigns_NotFound()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id, DonationTestKit.CampaignRequest("off", active: false));
        await kit.SeedCampaignAsync(app.Id, DonationTestKit.CampaignRequest("later", startsAt: DateTime.UtcNow.AddDays(1)));
        await kit.SeedCampaignAsync(app.Id, DonationTestKit.CampaignRequest("over",
            startsAt: DateTime.UtcNow.AddDays(-10), endsAt: DateTime.UtcNow.AddDays(-1)));

        foreach (var slug in new[] { "off", "later", "over", "missing" })
            await Assert.ThrowsAsync<NotFoundException>(() => kit.CheckoutAsync(app.Id, DonationTestKit.CheckoutRequest(slug: slug)));
        // No slug and no open campaign → 404 too.
        await Assert.ThrowsAsync<NotFoundException>(() => kit.CheckoutAsync(app.Id, DonationTestKit.CheckoutRequest(slug: null)));
    }

    [Fact]
    public async Task NoSlug_UsesSingleOpenCampaign_ButRequiresSlugWhenSeveral()
    {
        var appId = await SeedAsync();
        var ok = await kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(slug: null));
        Assert.NotNull((await kit.LoadDonationAsync(ok.DonationId)).CampaignId);

        await kit.SeedCampaignAsync(appId, DonationTestKit.CampaignRequest("second"));
        await Assert.ThrowsAsync<ValidationException>(() => kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(slug: null)));
    }

    [Fact]
    public async Task CampaignOfAnotherTenant_NotFound()
    {
        var appA = await SeedAsync();
        var appB = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(appB.Id);
        // "general" exists only in A.
        await Assert.ThrowsAsync<NotFoundException>(() => kit.CheckoutAsync(appB.Id, DonationTestKit.CheckoutRequest()));
        Assert.NotEqual(appA, appB.Id);
    }

    [Fact]
    public async Task SignedInAppUser_LinkedAndEmailDefaulted()
    {
        var appId = await SeedAsync();
        var user = await fixture.SeedAppUserAsync(appId);
        var result = await kit.CheckoutAsync(appId, DonationTestKit.CheckoutRequest(email: null), user.Id);
        var donation = await kit.LoadDonationAsync(result.DonationId);
        Assert.Equal(user.Id, donation.AppUserId);
        Assert.Equal(user.Email, donation.DonorEmail);
    }

    [Fact]
    public async Task ControllerCheckout_UsesAppUserOnlyForThisApp()
    {
        var app = await kit.SeedAppAsync();
        await kit.ConfigureProviderAsync(app.Id);
        await kit.SeedCampaignAsync(app.Id);
        await using var db = fixture.CreateContext();
        var controller = kit.ClientController(db, app, Guid.NewGuid());
        controller.ControllerContext = TestSupport.TestContexts.Client(app, Guid.NewGuid(), tokenAppId: Guid.NewGuid());
        var result = await controller.Checkout(DonationTestKit.CheckoutRequest(), CancellationToken.None);
        var body = Assert.IsType<CheckoutResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Null((await kit.LoadDonationAsync(body.DonationId)).AppUserId);
    }

    [Fact]
    public async Task Status_ScopedToApp()
    {
        var appId = await SeedAsync();
        var other = await kit.SeedAppAsync();
        var result = await kit.CheckoutAsync(appId);
        await using var db = fixture.CreateContext();
        Assert.Equal("pending", (await kit.Checkout(db).GetStatusAsync(appId, result.DonationId)).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => kit.Checkout(db).GetStatusAsync(other.Id, result.DonationId));
    }

    [Fact]
    public void FakeProvider_NotResolvableOutsideDevelopment()
    {
        var registry = new PaymentProviderRegistry([new FakePaymentProvider()], allowFake: false);
        Assert.False(registry.IsAvailable("fake"));
        Assert.Throws<NotFoundException>(() => registry.Get("fake"));
    }
}
