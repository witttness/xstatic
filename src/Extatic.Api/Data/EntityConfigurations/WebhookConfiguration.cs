using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class WebhookConfiguration : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> builder)
    {
        builder.ToTable("webhooks");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(w => w.Url).IsRequired().HasMaxLength(2048);
        builder.Property(w => w.Secret).IsRequired();
        builder.Property(w => w.Events).HasColumnType("text[]");
        builder.Property(w => w.IsActive).HasDefaultValue(true);
        builder.HasOne(w => w.App)
               .WithMany(a => a.Webhooks)
               .HasForeignKey(w => w.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
