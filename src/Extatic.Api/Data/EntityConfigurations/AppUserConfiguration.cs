using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(u => u.Provider).IsRequired().HasMaxLength(50);
        builder.Property(u => u.ProviderUserId).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasMaxLength(256);
        builder.Property(u => u.Metadata).HasColumnType("jsonb");
        builder.HasIndex(u => new { u.AppId, u.Provider, u.ProviderUserId }).IsUnique();
        builder.HasOne(u => u.App)
               .WithMany(a => a.AppUsers)
               .HasForeignKey(u => u.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
