using Extatic.Api.Modules;

namespace Extatic.Api.Features.SchemaLibrary;

public class SchemaLibraryModule : IFeatureModule
{
    public string Key => "schema-library";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        => services.AddScoped<SchemaLibraryService>();
}
