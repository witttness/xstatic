using Extatic.Api.Modules;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Extatic.Api.Features.Scheduling;

public class SchedulingModule : IFeatureModule
{
    public string Key => "scheduling";
    public IReadOnlyList<string> WebhookEvents => SchedulingEvents.All;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SchedulingNotifier>();
        services.AddScoped<SchedulingConfigService>();
        services.AddScoped<AppointmentService>();
        services.AddHostedService<AppointmentReminderWorker>();
    }
}
