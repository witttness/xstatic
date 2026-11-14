using Extatic.Api.Features.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<AuthSettings> AuthSettings => Set<AuthSettings>();
    public DbSet<AppUserCredential> AppUserCredentials => Set<AppUserCredential>();
    public DbSet<AppUserAuthToken> AppUserAuthTokens => Set<AppUserAuthToken>();
}
