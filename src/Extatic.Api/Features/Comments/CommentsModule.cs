using Extatic.Api.Modules;

namespace Extatic.Api.Features.Comments;

public class CommentsModule : IFeatureModule
{
    public string Key => "comments";

    public IReadOnlyList<string> WebhookEvents => CommentNotifier.Events;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CommentNotifier>();
        services.AddScoped<CommentService>();
        services.AddScoped<CommentModerationService>();
    }
}
