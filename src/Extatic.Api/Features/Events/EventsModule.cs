using Extatic.Api.Modules;

namespace Extatic.Api.Features.Events;

public class EventsModule : IFeatureModule
{
    public string Key => "events";

    public IReadOnlyList<string> WebhookEvents => EventNotifications.WebhookEvents;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EventNotifications>();
        services.AddScoped<EventService>();
    }
}
