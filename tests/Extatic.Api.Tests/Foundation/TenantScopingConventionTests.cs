using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Extatic.Api.Tests.Foundation;

/// <summary>
/// Every EF entity must belong to a tenant (implement <see cref="IAppScoped"/>) so queries can be
/// scoped by AppId. The build fails for any new entity that does not.
/// </summary>
public class TenantScopingConventionTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    /// <summary>
    /// Entities that are intentionally NOT IAppScoped. FEATURE AUTHORS: DO NOT add to this list.
    /// Give your entity an AppId and implement IAppScoped instead. A child row reached only through
    /// a scoped parent still gets its own AppId (denormalised) so it can be filtered directly.
    /// The only acceptable additions are genuinely platform-global tables (e.g. a schema library
    /// curated by platform admins), and each needs a one-line justification comment and lead review.
    /// </summary>
    private static readonly HashSet<Type> NotTenantScopedAllowlist =
    [
        typeof(User),                // platform account; spans many Apps
        typeof(App),                 // the tenant itself
        typeof(Item),                // pre-existing; scoped via Collection.AppId
        typeof(Attachment),          // pre-existing; scoped via Item → Collection
        typeof(WebhookDeliveryLog),  // pre-existing; scoped via Webhook.AppId
        typeof(DataProtectionKey),   // ASP.NET Data Protection key ring (platform-wide)
        // Schema Library: global library schemas are deliberately platform-scoped (AppId null, curated
        // by platform admins), so these rows cannot implement IAppScoped (non-nullable AppId). App-scope
        // rows carry their AppId and every App-scope query filters by it (see LibrarySchemaAccess).
        typeof(Extatic.Api.Features.SchemaLibrary.LibrarySchema),
        typeof(Extatic.Api.Features.SchemaLibrary.LibrarySchemaVersion),
    ];

    [Fact]
    public void EveryEntityIsAppScopedOrAllowlisted()
    {
        using var db = fixture.CreateContext();
        var offenders = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType)
            .Select(e => e.ClrType)
            .Where(t => !typeof(IAppScoped).IsAssignableFrom(t) && !NotTenantScopedAllowlist.Contains(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Entities must implement IAppScoped (Extatic.Api.Domain): " + string.Join(", ", offenders));
    }
}
