using Extatic.Api.Features.Search;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<SearchDocument> SearchDocuments => Set<SearchDocument>();
    public DbSet<SearchCollectionConfig> SearchCollectionConfigs => Set<SearchCollectionConfig>();
    public DbSet<SearchSettings> SearchSettings => Set<SearchSettings>();
    public DbSet<SearchQueryLog> SearchQueryLogs => Set<SearchQueryLog>();
    public DbSet<SearchReindexJob> SearchReindexJobs => Set<SearchReindexJob>();
}
