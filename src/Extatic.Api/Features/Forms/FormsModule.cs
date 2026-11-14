using Extatic.Api.Modules;

namespace Extatic.Api.Features.Forms;

public class FormsModule : IFeatureModule
{
    public string Key => "forms";
    public IReadOnlyList<string> WebhookEvents => [FormSubmissionService.WebhookEvent];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FormService>();
        services.AddScoped<FormSubmissionService>();
    }
}
