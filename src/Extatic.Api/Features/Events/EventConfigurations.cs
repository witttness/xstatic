using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Events;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(10000);
        builder.Property(e => e.Location).HasMaxLength(500);
        builder.Property(e => e.OnlineUrl).HasMaxLength(2048);
        builder.Property(e => e.Timezone).IsRequired().HasMaxLength(100);
        builder.Property(e => e.MaxSeatsPerRegistration).HasDefaultValue(1);
        builder.Property(e => e.RegistrationSchema).HasColumnType("jsonb");
        builder.Property(e => e.ManageUrlTemplate).HasMaxLength(2048);
        builder.Property(e => e.NotifyEmails).HasColumnType("text[]");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.AppId, e.Slug }).IsUnique();
        builder.HasIndex(e => new { e.AppId, e.Status, e.StartsAt });
        builder.HasOne(e => e.App)
               .WithMany()
               .HasForeignKey(e => e.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EventRegistrationConfiguration : IEntityTypeConfiguration<EventRegistration>
{
    public void Configure(EntityTypeBuilder<EventRegistration> builder)
    {
        builder.ToTable("event_registrations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Email).IsRequired().HasMaxLength(320);
        builder.Property(r => r.Data).IsRequired().HasColumnType("jsonb");
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ManageTokenHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(r => r.ManageTokenHash).IsUnique();
        // One active (non-cancelled) registration per email per event.
        builder.HasIndex(r => new { r.EventId, r.Email })
               .IsUnique()
               .HasFilter("\"Status\" <> 'Cancelled'")
               .HasDatabaseName("IX_event_registrations_EventId_Email_active");
        builder.HasIndex(r => new { r.EventId, r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.AppId, r.AppUserId });
        builder.HasOne(r => r.App)
               .WithMany()
               .HasForeignKey(r => r.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Event)
               .WithMany(e => e.Registrations)
               .HasForeignKey(r => r.EventId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.AppUser)
               .WithMany()
               .HasForeignKey(r => r.AppUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
