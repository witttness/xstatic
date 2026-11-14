using Extatic.Api.Features.SchemaLibrary;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<LibrarySchema> LibrarySchemas => Set<LibrarySchema>();
    public DbSet<LibrarySchemaVersion> LibrarySchemaVersions => Set<LibrarySchemaVersion>();
}
