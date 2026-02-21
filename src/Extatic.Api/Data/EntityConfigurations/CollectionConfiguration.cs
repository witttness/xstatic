using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("collections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Schema).HasColumnType("jsonb");
        builder.Property(c => c.AllowedAttachmentTypes).HasColumnType("text[]");
        builder.HasIndex(c => new { c.AppId, c.Slug }).IsUnique();
        builder.HasOne(c => c.App)
               .WithMany(a => a.Collections)
               .HasForeignKey(c => c.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
