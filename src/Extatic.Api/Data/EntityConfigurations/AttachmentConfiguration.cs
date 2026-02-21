using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Filename).IsRequired().HasMaxLength(512);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(256);
        builder.Property(a => a.StoragePath).IsRequired().HasMaxLength(1024);
        builder.Property(a => a.Url).IsRequired().HasMaxLength(2048);
        builder.HasOne(a => a.Item)
               .WithMany(i => i.Attachments)
               .HasForeignKey(a => a.ItemId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.AppUser)
               .WithMany(u => u.Attachments)
               .HasForeignKey(a => a.AppUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
