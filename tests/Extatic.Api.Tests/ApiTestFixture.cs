using System.Security.Claims;
using Extatic.Api.Auth;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Extatic.Api.Tests;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly string connectionString;
    private readonly SemaphoreSlim initializeLock = new(1, 1);
    private bool initialized;

    public ApiTestFixture()
    {
        var baseConnection = Environment.GetEnvironmentVariable("EXTATIC_TEST_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=extatic;Username=extatic;Password=extatic";
        var builder = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = $"extatic_test_{Guid.NewGuid():N}"
        };
        connectionString = builder.ConnectionString;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }

    public IServiceScopeFactory CreateScopeFactory()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => CreateContext())
            .BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    public async Task EnsureDatabaseAsync()
    {
        await initializeLock.WaitAsync();
        try
        {
            if (initialized) return;
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
            initialized = true;
        }
        finally
        {
            initializeLock.Release();
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task PrepareLegacyDatabaseAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync("20260312022705_AddAppPublicId");
        initialized = true;
    }

    public async Task<(User Owner, App App, Collection Collection)> SeedAppAsync(
        string? schema = null)
    {
        await EnsureDatabaseAsync();
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var owner = new User
        {
            ExternalId = $"owner-{suffix}",
            Email = $"owner-{suffix}@example.test",
            Name = "Owner"
        };
        var app = new App
        {
            Owner = owner,
            Name = "Test App",
            Slug = $"app-{suffix}",
            PublicId = $"app_{suffix}",
            ApiKeyHash = suffix
        };
        var collection = new Collection
        {
            App = app,
            Name = "Items",
            Slug = "items",
            Schema = schema
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        return (owner, app, collection);
    }

    public async Task<AppUser> SeedAppUserAsync(Guid appId)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser
        {
            AppId = appId,
            Provider = "test",
            ProviderUserId = suffix,
            Email = $"{suffix}@example.test"
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<Collaborator> SeedCollaboratorAsync(Guid appId, CollaboratorRole role,
        bool accepted = true)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = new User
        {
            ExternalId = $"member-{suffix}",
            Email = $"member-{suffix}@example.test",
            Name = "Member"
        };
        var collaborator = new Collaborator
        {
            AppId = appId,
            User = user,
            Role = role,
            AcceptedAt = accepted ? DateTime.UtcNow : null
        };
        db.Collaborators.Add(collaborator);
        await db.SaveChangesAsync();
        return collaborator;
    }

    public async Task DisposeAsync()
    {
        if (initialized)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
        initializeLock.Dispose();
    }

    public static ClaimsPrincipal PlatformPrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(AppClaimTypes.UserId, userId.ToString())], AuthSchemes.OAuthProxy);
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal AppUserPrincipal(Guid appUserId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(AppClaimTypes.AppUserId, appUserId.ToString())], AuthSchemes.AppUser);
        return new ClaimsPrincipal(identity);
    }
}
