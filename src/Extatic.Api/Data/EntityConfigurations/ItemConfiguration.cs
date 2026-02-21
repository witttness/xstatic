using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.Data).IsRequired().HasColumnType("jsonb");
        builder.HasIndex(i => i.CollectionId);
        builder.HasIndex(i => i.AppUserId);
        builder.HasOne(i => i.Collection)
               .WithMany(c => c.Items)
               .HasForeignKey(i => i.CollectionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.AppUser)
               .WithMany(u => u.Items)
               .HasForeignKey(i => i.AppUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
