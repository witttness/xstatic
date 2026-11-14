namespace Extatic.Api.Notifications;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddEmailNotifications(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailQueue, EmailQueue>();
        if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]))
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        else
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDeliveryWorker>();
        return services;
    }
}
