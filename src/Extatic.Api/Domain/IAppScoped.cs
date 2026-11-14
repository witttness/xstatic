namespace Extatic.Api.Domain;

/// <summary>
/// Marks an entity as belonging to exactly one tenant (App). Every query for an IAppScoped entity
/// must filter by <see cref="AppId"/>. <c>TenantScopingConventionTests</c> fails the build for any
/// EF entity that neither implements this nor is on its explicit allowlist.
/// </summary>
public interface IAppScoped
{
    Guid AppId { get; }
}
