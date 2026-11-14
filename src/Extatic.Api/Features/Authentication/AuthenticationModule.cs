using Extatic.Api.Modules;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Extatic.Api.Features.Authentication;

public class AuthenticationModule : IFeatureModule
{
    public string Key => "authentication";

    public IReadOnlyList<string> WebhookEvents =>
        ["appuser.registered", "appuser.verified", "appuser.login", "appuser.deleted"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuthSettingsService>();
        services.AddScoped<PasswordAuthService>();
        services.AddHostedService<AuthTokenCleanupService>();
    }
}
