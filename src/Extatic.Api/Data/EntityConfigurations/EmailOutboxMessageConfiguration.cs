using Extatic.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Data.EntityConfigurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.ToAddress).IsRequired().HasMaxLength(320);
        builder.Property(m => m.Subject).IsRequired().HasMaxLength(500);
        builder.Property(m => m.TextBody).IsRequired();
        builder.Property(m => m.ReplyTo).HasMaxLength(320);
        builder.Property(m => m.Category).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasOne(m => m.App)
               .WithMany()
               .HasForeignKey(m => m.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
