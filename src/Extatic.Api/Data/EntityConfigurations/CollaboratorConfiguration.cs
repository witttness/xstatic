using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class CollaboratorConfiguration : IEntityTypeConfiguration<Collaborator>
{
    public void Configure(EntityTypeBuilder<Collaborator> builder)
    {
        builder.ToTable("collaborators");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Role).IsRequired();
        builder.HasIndex(c => new { c.AppId, c.UserId }).IsUnique();
        builder.HasOne(c => c.App)
               .WithMany(a => a.Collaborators)
               .HasForeignKey(c => c.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.User)
               .WithMany(u => u.Collaborations)
               .HasForeignKey(c => c.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
