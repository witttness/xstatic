using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Members;

public class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        builder.ToTable("membership_plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.Name).IsRequired().HasMaxLength(MembersLimits.PlanNameMax);
        builder.Property(p => p.Slug).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(MembersLimits.PlanDescriptionMax);
        builder.Property(p => p.PriceLabel).HasMaxLength(MembersLimits.PriceLabelMax);
        builder.Property(p => p.Benefits)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    v => v.ToList()))
            .IsRequired();
        builder.HasIndex(p => new { p.AppId, p.Slug }).IsUnique();
        builder.HasOne(p => p.App)
               .WithMany()
               .HasForeignKey(p => p.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.MemberNumber).HasMaxLength(50);
        builder.Property(m => m.Profile).IsRequired().HasColumnType("jsonb");
        builder.Property(m => m.Notes).HasMaxLength(MembersLimits.NotesMax);
        builder.Property(m => m.RemindersSent).HasColumnType("integer[]").IsRequired();

        // At most one non-terminal membership per AppUser per App.
        builder.HasIndex(m => new { m.AppId, m.AppUserId })
               .IsUnique()
               .HasFilter("\"Status\" IN ('Pending', 'Active', 'Suspended')")
               .HasDatabaseName("IX_memberships_AppId_AppUserId_open");
        builder.HasIndex(m => new { m.AppId, m.MemberNumber }).IsUnique();
        builder.HasIndex(m => new { m.AppId, m.Status, m.ExpiresAt });
        builder.HasIndex(m => m.PlanId);
        builder.HasIndex(m => m.AppUserId);

        builder.HasOne(m => m.App)
               .WithMany()
               .HasForeignKey(m => m.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.AppUser)
               .WithMany()
               .HasForeignKey(m => m.AppUserId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Plan)
               .WithMany()
               .HasForeignKey(m => m.PlanId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class MemberSettingsConfiguration : IEntityTypeConfiguration<MemberSettings>
{
    public void Configure(EntityTypeBuilder<MemberSettings> builder)
    {
        builder.ToTable("member_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.ProfileSchema).HasColumnType("jsonb");
        builder.Property(s => s.DirectoryFields).HasColumnType("text[]").IsRequired();
        builder.Property(s => s.MemberNumberPrefix).IsRequired().HasMaxLength(MembersLimits.PrefixMax);
        builder.Property(s => s.NotifyEmails).HasColumnType("text[]").IsRequired();
        builder.Property(s => s.RenewalReminderDays).HasColumnType("integer[]").IsRequired();
        builder.HasIndex(s => s.AppId).IsUnique();
        builder.HasOne(s => s.App)
               .WithMany()
               .HasForeignKey(s => s.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MembershipEventConfiguration : IEntityTypeConfiguration<MembershipEvent>
{
    public void Configure(EntityTypeBuilder<MembershipEvent> builder)
    {
        builder.ToTable("membership_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Type).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Detail).HasMaxLength(2000);
        builder.HasIndex(e => new { e.AppId, e.MembershipId, e.CreatedAt });
        builder.HasOne(e => e.App)
               .WithMany()
               .HasForeignKey(e => e.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Membership)
               .WithMany(m => m.Events)
               .HasForeignKey(e => e.MembershipId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
