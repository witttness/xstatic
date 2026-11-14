using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Modules;

namespace Extatic.Api.Features.Donations;

public class DonationsModule : IFeatureModule
{
    public string Key => "donations";

    public IReadOnlyList<string> WebhookEvents =>
        [DonationNotifier.EventSucceeded, DonationNotifier.EventFailed, DonationNotifier.EventRefunded];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(StripePaymentProvider.HttpClientName, client =>
            {
                client.BaseAddress = StripePaymentProvider.ApiBase;
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton<StripePaymentProvider>();
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<StripePaymentProvider>());
        // Registered always, but PaymentProviderRegistry only exposes it in Development.
        services.AddSingleton<FakePaymentProvider>();
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());
        services.AddSingleton(sp => new PaymentProviderRegistry(
            sp.GetServices<IPaymentProvider>(), sp.GetRequiredService<IHostEnvironment>()));

        services.AddScoped<DonationNotifier>();
        services.AddScoped<PaymentProviderConfigService>();
        services.AddScoped<DonationCampaignService>();
        services.AddScoped<DonationCheckoutService>();
        services.AddScoped<DonationWebhookService>();
        services.AddScoped<DonationAdminService>();
    }
}
