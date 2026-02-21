using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class WebhookDeliveryLogConfiguration : IEntityTypeConfiguration<WebhookDeliveryLog>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryLog> builder)
    {
        builder.ToTable("webhook_delivery_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.EventType).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(l => l.ResponseBody).HasMaxLength(4096);
        builder.HasIndex(l => l.NextRetryAt);
        builder.HasIndex(l => l.CreatedAt);
        builder.HasOne(l => l.Webhook)
               .WithMany(w => w.DeliveryLogs)
               .HasForeignKey(l => l.WebhookId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
