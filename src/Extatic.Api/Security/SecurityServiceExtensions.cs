using Extatic.Api.Data;
using Microsoft.AspNetCore.DataProtection;

namespace Extatic.Api.Security;

public static class SecurityServiceExtensions
{
    public static IServiceCollection AddTenantSecretProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName("Extatic")
            .PersistKeysToDbContext<AppDbContext>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        return services;
    }
}
