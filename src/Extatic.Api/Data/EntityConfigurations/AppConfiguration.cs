using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class AppConfiguration : IEntityTypeConfiguration<App>
{
    public void Configure(EntityTypeBuilder<App> builder)
    {
        builder.ToTable("apps");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Name).IsRequired().HasMaxLength(256);
        builder.Property(a => a.Slug).IsRequired().HasMaxLength(100);
        builder.Property(a => a.PublicId).IsRequired().HasMaxLength(24);
        builder.HasIndex(a => a.PublicId).IsUnique();
        builder.Property(a => a.ApiKeyHash).IsRequired();
        builder.Property(a => a.AllowedOrigins).HasColumnType("text[]");
        builder.Property(a => a.MaxFileSizeMb).HasDefaultValue(10);
        builder.Property(a => a.MaxAttachmentsPerItem).HasDefaultValue(10);
        builder.Property(a => a.StorageQuotaGb).HasDefaultValue(1);
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.HasOne(a => a.Owner)
               .WithMany(u => u.OwnedApps)
               .HasForeignKey(a => a.OwnerId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
