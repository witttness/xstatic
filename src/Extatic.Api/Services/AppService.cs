using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class AppService(AppDbContext db)
{
    public async Task<List<App>> GetForUserAsync(Guid userId)
    {
        var ownedApps = await db.Apps
            .Where(a => a.OwnerId == userId)
            .ToListAsync();

        var collaboratedApps = await db.Apps
            .Where(a => a.Collaborators.Any(c => c.UserId == userId && c.AcceptedAt != null))
            .ToListAsync();

        return ownedApps.Concat(collaboratedApps).DistinctBy(a => a.Id).ToList();
    }

    public async Task<App> GetBySlugAsync(string slug)
    {
        var app = await db.Apps.FirstOrDefaultAsync(a => a.Slug == slug);
        return app ?? throw new NotFoundException($"App '{slug}' not found");
    }

    public async Task<(App App, string RawApiKey)> CreateAsync(Guid ownerId, string name, string slug)
    {
        if (await db.Apps.AnyAsync(a => a.Slug == slug))
            throw new ConflictException($"App slug '{slug}' is already taken");

        var (rawKey, hash) = GenerateApiKey();
        var app = new App
        {
            OwnerId = ownerId,
            Name = name,
            Slug = slug,
            ApiKeyHash = hash
        };
        db.Apps.Add(app);
        await db.SaveChangesAsync();
        return (app, rawKey);
    }

    public async Task<App> UpdateAsync(App app, string? name, string[]? allowedOrigins,
        int? maxFileSizeMb, int? maxAttachmentsPerItem, int? storageQuotaGb)
    {
        if (name is not null) app.Name = name;
        if (allowedOrigins is not null) app.AllowedOrigins = allowedOrigins;
        if (maxFileSizeMb is not null) app.MaxFileSizeMb = maxFileSizeMb.Value;
        if (maxAttachmentsPerItem is not null) app.MaxAttachmentsPerItem = maxAttachmentsPerItem.Value;
        if (storageQuotaGb is not null) app.StorageQuotaGb = storageQuotaGb.Value;
        await db.SaveChangesAsync();
        return app;
    }

    public async Task DeleteAsync(App app)
    {
        db.Apps.Remove(app);
        await db.SaveChangesAsync();
    }

    public async Task<string> RegenerateApiKeyAsync(App app)
    {
        var (rawKey, hash) = GenerateApiKey();
        app.ApiKeyHash = hash;
        await db.SaveChangesAsync();
        return rawKey;
    }

    private static (string RawKey, string Hash) GenerateApiKey()
    {
        var raw = $"exk_{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=')}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return (raw, hash);
    }
}
