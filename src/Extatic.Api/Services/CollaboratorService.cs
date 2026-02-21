using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Services;

public class CollaboratorService(AppDbContext db)
{
    public async Task<List<Collaborator>> GetForAppAsync(Guid appId)
        => await db.Collaborators
            .Include(c => c.User)
            .Where(c => c.AppId == appId)
            .ToListAsync();

    public async Task<Collaborator> InviteAsync(Guid appId, string email, CollaboratorRole role, Guid invitedBy)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
            throw new NotFoundException($"No user found with email '{email}'");

        if (await db.Collaborators.AnyAsync(c => c.AppId == appId && c.UserId == user.Id))
            throw new ConflictException("User is already a collaborator on this app");

        var collaborator = new Collaborator
        {
            AppId = appId,
            UserId = user.Id,
            Role = role,
            InvitedBy = invitedBy
        };
        db.Collaborators.Add(collaborator);
        await db.SaveChangesAsync();
        return collaborator;
    }

    public async Task<Collaborator> AcceptInvitationAsync(Guid appId, Guid userId)
    {
        var collaborator = await db.Collaborators
            .FirstOrDefaultAsync(c => c.AppId == appId && c.UserId == userId);
        if (collaborator is null)
            throw new NotFoundException("No pending invitation found");
        if (collaborator.AcceptedAt is not null)
            throw new ConflictException("Invitation already accepted");

        collaborator.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return collaborator;
    }

    public async Task<Collaborator> UpdateRoleAsync(Guid appId, Guid collaboratorId, CollaboratorRole role)
    {
        var collaborator = await db.Collaborators
            .FirstOrDefaultAsync(c => c.AppId == appId && c.Id == collaboratorId);
        if (collaborator is null)
            throw new NotFoundException($"Collaborator {collaboratorId} not found");

        collaborator.Role = role;
        await db.SaveChangesAsync();
        return collaborator;
    }

    public async Task RemoveAsync(Guid appId, Guid collaboratorId)
    {
        var collaborator = await db.Collaborators
            .FirstOrDefaultAsync(c => c.AppId == appId && c.Id == collaboratorId);
        if (collaborator is null)
            throw new NotFoundException($"Collaborator {collaboratorId} not found");

        db.Collaborators.Remove(collaborator);
        await db.SaveChangesAsync();
    }
}
