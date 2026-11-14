using Extatic.Api.Modules;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Extatic.Api.Features.Members;

public class MembersModule : IFeatureModule
{
    public string Key => "members";

    public IReadOnlyList<string> WebhookEvents =>
    [
        "member.applied", "member.activated", "member.suspended", "member.reinstated",
        "member.renewed", "member.expired", "member.cancelled"
    ];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<MembershipService>();
        services.AddScoped<MembershipMaintenance>();
        services.AddHostedService<MembershipMaintenanceWorker>();
    }
}
