using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class UserService(AppDbContext db)
{
    public async Task<User> UpsertAsync(string externalId, string email, string? name = null)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);
        if (user is null)
        {
            user = new User
            {
                ExternalId = externalId,
                Email = email,
                Name = name ?? email.Split('@')[0]
            };
            db.Users.Add(user);
        }
        else
        {
            user.Email = email;
            if (name is not null) user.Name = name;
        }

        await db.SaveChangesAsync();
        return user;
    }

    public async Task<User> GetByIdAsync(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        return user ?? throw new NotFoundException($"User {id} not found");
    }
}
