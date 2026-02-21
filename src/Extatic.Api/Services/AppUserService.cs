using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class AppUserService(AppDbContext db)
{
    public async Task<List<AppUser>> GetForAppAsync(Guid appId)
        => await db.AppUsers.Where(u => u.AppId == appId).ToListAsync();

    public async Task<AppUser> UpsertAsync(Guid appId, string provider, string providerUserId,
        string? email, string? displayName, string? avatarUrl)
    {
        var appUser = await db.AppUsers
            .FirstOrDefaultAsync(u => u.AppId == appId
                && u.Provider == provider
                && u.ProviderUserId == providerUserId);

        if (appUser is null)
        {
            appUser = new AppUser
            {
                AppId = appId,
                Provider = provider,
                ProviderUserId = providerUserId,
                Email = email,
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                LastLoginAt = DateTime.UtcNow
            };
            db.AppUsers.Add(appUser);
        }
        else
        {
            if (email is not null) appUser.Email = email;
            if (displayName is not null) appUser.DisplayName = displayName;
            if (avatarUrl is not null) appUser.AvatarUrl = avatarUrl;
            appUser.LastLoginAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return appUser;
    }

    public async Task<AppUser> GetByIdAsync(Guid id)
    {
        var user = await db.AppUsers.FindAsync(id);
        return user ?? throw new NotFoundException($"AppUser {id} not found");
    }
}
