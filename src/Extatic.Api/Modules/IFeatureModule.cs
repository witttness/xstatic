namespace Extatic.Api.Modules;

/// <summary>
/// A self-contained feature (Forms, Events, Comments, ...). Implementations are discovered by
/// reflection at startup (<see cref="FeatureModuleExtensions.AddFeatureModules"/>), so adding a
/// feature never requires editing Program.cs. Controllers are discovered by MVC automatically;
/// the module registers the feature's services, hosted services and HttpClients.
/// </summary>
/// <remarks>Implementations must be concrete, public and have a public parameterless constructor.</remarks>
public interface IFeatureModule
{
    /// <summary>Stable, lowercase identifier for the feature, e.g. "forms".</summary>
    string Key { get; }

    /// <summary>Webhook event types this feature can emit (e.g. "form.submitted").</summary>
    IReadOnlyList<string> WebhookEvents => [];

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
