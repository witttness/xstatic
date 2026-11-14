using Extatic.Api.Modules;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Extatic.Api.Tests.Foundation;

public class FeatureModuleTests
{
    [Fact]
    public void DiscoverModules_FindsConcreteModulesWithParameterlessCtor()
    {
        var modules = FeatureModuleExtensions.DiscoverModules(typeof(FeatureModuleTests).Assembly);

        Assert.Contains(modules, m => m is TestModule);
        Assert.DoesNotContain(modules, m => m.Key == "abstract" || m.Key == "needs-args");
    }

    [Fact]
    public void AddFeatureModules_ConfiguresServicesRegistersModulesAndCatalog()
    {
        var services = new ServiceCollection();
        services.AddFeatureModules(TestServices.Configuration(), typeof(FeatureModuleTests).Assembly);
        using var provider = services.BuildServiceProvider();

        Assert.Equal("configured", provider.GetRequiredService<TestModuleMarker>().Value);
        Assert.Contains(provider.GetServices<IFeatureModule>(), m => m.Key == "test-module");
        var catalog = provider.GetRequiredService<WebhookEventCatalog>();
        Assert.Equal(["item.created", "item.updated", "item.deleted", "test.happened"], catalog.Events);
    }

    [Fact]
    public void AddFeatureModules_ApiAssembly_RegistersCatalogWithCoreEvents()
    {
        var services = new ServiceCollection();
        services.AddFeatureModules(TestServices.Configuration());
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<WebhookEventCatalog>();
        Assert.True(catalog.IsKnown("item.created"));
        Assert.False(catalog.IsKnown("nope.never"));
    }

    public sealed class TestModuleMarker(string value) { public string Value { get; } = value; }

    public sealed class TestModule : IFeatureModule
    {
        public string Key => "test-module";
        public IReadOnlyList<string> WebhookEvents => ["test.happened", "item.created"];
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
            => services.AddSingleton(new TestModuleMarker("configured"));
    }

    public abstract class AbstractModule : IFeatureModule
    {
        public string Key => "abstract";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    public sealed class ModuleNeedingArgs : IFeatureModule
    {
        public ModuleNeedingArgs(string key) { }
        public string Key => "needs-args";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }
}
