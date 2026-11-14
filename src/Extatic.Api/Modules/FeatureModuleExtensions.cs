using System.Reflection;
using Extatic.Api.Webhooks;

namespace Extatic.Api.Modules;

public static class FeatureModuleExtensions
{
    /// <summary>
    /// Discovers every public concrete <see cref="IFeatureModule"/> in the API assembly, lets each one
    /// register its services, registers each instance as a singleton <see cref="IFeatureModule"/>,
    /// and registers the <see cref="WebhookEventCatalog"/>.
    /// </summary>
    public static IServiceCollection AddFeatureModules(this IServiceCollection services,
        IConfiguration configuration)
        => services.AddFeatureModules(configuration, typeof(IFeatureModule).Assembly);

    public static IServiceCollection AddFeatureModules(this IServiceCollection services,
        IConfiguration configuration, Assembly assembly)
    {
        var modules = DiscoverModules(assembly);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (!keys.Add(module.Key))
                throw new InvalidOperationException($"Duplicate feature module key '{module.Key}'");
            module.ConfigureServices(services, configuration);
            services.AddSingleton(module);
        }
        services.AddSingleton<WebhookEventCatalog>();
        return services;
    }

    public static IReadOnlyList<IFeatureModule> DiscoverModules(Assembly assembly)
        => assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false, IsVisible: true }
                && typeof(IFeatureModule).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IFeatureModule)Activator.CreateInstance(t)!)
            .ToList();
}
