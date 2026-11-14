using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Donations;

public class PaymentProviderConfigConfiguration : IEntityTypeConfiguration<PaymentProviderConfig>
{
    public void Configure(EntityTypeBuilder<PaymentProviderConfig> builder)
    {
        builder.ToTable("payment_provider_configs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Provider).IsRequired().HasMaxLength(50);
        builder.Property(c => c.PublishableKey).HasMaxLength(255);
        builder.Property(c => c.SecretKeyProtected).IsRequired();
        builder.Property(c => c.SecretKeyLast4).IsRequired().HasMaxLength(4);
        builder.Property(c => c.WebhookSecretLast4).HasMaxLength(4);
        builder.HasIndex(c => new { c.AppId, c.Provider }).IsUnique();
        builder.HasOne(c => c.App).WithMany().HasForeignKey(c => c.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DonationCampaignConfiguration : IEntityTypeConfiguration<DonationCampaign>
{
    public void Configure(EntityTypeBuilder<DonationCampaign> builder)
    {
        builder.ToTable("donation_campaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(5000);
        builder.Property(c => c.Currency).IsRequired().HasMaxLength(3);
        builder.Property(c => c.SuccessUrl).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.CancelUrl).IsRequired().HasMaxLength(2000);
        builder.HasIndex(c => new { c.AppId, c.Slug }).IsUnique();
        builder.HasOne(c => c.App).WithMany().HasForeignKey(c => c.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        builder.ToTable("donations", t =>
        {
            t.HasCheckConstraint("CK_donations_amount_positive", "\"AmountMinor\" > 0");
            t.HasCheckConstraint("CK_donations_refund_range",
                "\"RefundedAmountMinor\" >= 0 AND \"RefundedAmountMinor\" <= \"AmountMinor\"");
        });
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.Currency).IsRequired().HasMaxLength(3);
        builder.Property(d => d.Interval).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.DonorName).HasMaxLength(100);
        builder.Property(d => d.DonorEmail).HasMaxLength(320);
        builder.Property(d => d.Message).HasMaxLength(500);
        builder.Property(d => d.Provider).IsRequired().HasMaxLength(50);
        builder.Property(d => d.ProviderSessionId).HasMaxLength(255);
        builder.Property(d => d.ProviderPaymentId).HasMaxLength(255);
        builder.Property(d => d.ProviderSubscriptionId).HasMaxLength(255);
        builder.Property(d => d.IpHash).IsRequired().HasMaxLength(64);
        builder.Ignore(d => d.NetAmountMinor);
        builder.HasIndex(d => new { d.Provider, d.ProviderSessionId }).IsUnique();
        builder.HasIndex(d => new { d.AppId, d.Provider, d.ProviderPaymentId });
        builder.HasIndex(d => new { d.AppId, d.CampaignId, d.Status });
        builder.HasIndex(d => new { d.AppId, d.CreatedAt });
        builder.HasOne(d => d.App).WithMany().HasForeignKey(d => d.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Campaign).WithMany().HasForeignKey(d => d.CampaignId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.AppUser).WithMany().HasForeignKey(d => d.AppUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ProviderEventId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Type).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => new { e.AppId, e.Provider, e.ProviderEventId }).IsUnique();
        builder.HasOne(e => e.App).WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}
