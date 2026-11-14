using Extatic.Api.Modules;
using Extatic.Api.Services;

namespace Extatic.Api.Features.Search;

public class SearchModule : IFeatureModule
{
    public string Key => "search";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SearchQueryService>();
        services.AddScoped<SearchDocumentService>();
        services.AddScoped<SearchAdminService>();
        services.AddScoped<IItemChangeListener, SearchItemChangeListener>();
        services.AddHostedService<SearchReindexWorker>();
        services.AddHostedService<SearchQueryLogCleanupWorker>();
    }
}
