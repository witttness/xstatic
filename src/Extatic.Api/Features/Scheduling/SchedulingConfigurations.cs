using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Scheduling;

public class BookingCalendarConfiguration : IEntityTypeConfiguration<BookingCalendar>
{
    public void Configure(EntityTypeBuilder<BookingCalendar> builder)
    {
        builder.ToTable("booking_calendars");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Timezone).IsRequired().HasMaxLength(100);
        builder.Property(c => c.NotifyEmails).HasColumnType("text[]");
        builder.HasIndex(c => new { c.AppId, c.Slug }).IsUnique();
        builder.HasOne(c => c.App).WithMany().HasForeignKey(c => c.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppointmentTypeConfiguration : IEntityTypeConfiguration<AppointmentType>
{
    public void Configure(EntityTypeBuilder<AppointmentType> builder)
    {
        builder.ToTable("appointment_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Slug).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Description).HasMaxLength(5000);
        builder.Property(t => t.IntakeSchema).HasColumnType("jsonb");
        builder.Property(t => t.Location).HasMaxLength(500);
        builder.Property(t => t.MeetingUrl).HasMaxLength(2000);
        builder.Property(t => t.ManageUrlTemplate).HasMaxLength(2000);
        builder.HasIndex(t => new { t.AppId, t.Slug }).IsUnique();
        builder.HasIndex(t => t.CalendarId);
        builder.HasOne(t => t.App).WithMany().HasForeignKey(t => t.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Calendar).WithMany(c => c.AppointmentTypes)
               .HasForeignKey(t => t.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("availability_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.DayOfWeek).HasConversion<int>();
        builder.HasIndex(r => new { r.CalendarId, r.DayOfWeek });
        builder.HasOne(r => r.App).WithMany().HasForeignKey(r => r.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Calendar).WithMany(c => c.Rules)
               .HasForeignKey(r => r.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AvailabilityOverrideConfiguration : IEntityTypeConfiguration<AvailabilityOverride>
{
    public void Configure(EntityTypeBuilder<AvailabilityOverride> builder)
    {
        builder.ToTable("availability_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.Reason).HasMaxLength(500);
        builder.HasIndex(o => new { o.CalendarId, o.Date }).IsUnique();
        builder.HasOne(o => o.App).WithMany().HasForeignKey(o => o.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(o => o.Calendar).WithMany(c => c.Overrides)
               .HasForeignKey(o => o.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.CustomerEmail).IsRequired().HasMaxLength(320);
        builder.Property(a => a.CustomerPhone).HasMaxLength(50);
        builder.Property(a => a.CustomerTimezone).HasMaxLength(100);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Data).IsRequired().HasColumnType("jsonb");
        builder.Property(a => a.Notes).HasMaxLength(5000);
        builder.Property(a => a.ManageTokenHash).IsRequired().HasMaxLength(64);
        builder.Property(a => a.CancelReason).HasMaxLength(1000);
        builder.HasIndex(a => a.ManageTokenHash).IsUnique();
        builder.HasIndex(a => new { a.AppId, a.StartsAt });
        builder.HasIndex(a => new { a.CalendarId, a.BlockStartsAt });
        builder.HasIndex(a => a.AppUserId);
        // Partial index for the reminder worker's due scan.
        builder.HasIndex(a => a.StartsAt).HasFilter("\"ReminderSentAt\" IS NULL AND \"Status\" = 'Confirmed'")
               .HasDatabaseName("ix_appointments_reminder_due");
        builder.HasOne(a => a.App).WithMany().HasForeignKey(a => a.AppId).OnDelete(DeleteBehavior.Cascade);
        // NO ACTION (checked at statement end) rather than RESTRICT so App deletion can cascade through
        // calendars/types and appointments in one statement; deleting a calendar or type that still has
        // appointments is rejected by the services with 409 before reaching the database.
        builder.HasOne(a => a.Calendar).WithMany()
               .HasForeignKey(a => a.CalendarId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.AppointmentType).WithMany()
               .HasForeignKey(a => a.AppointmentTypeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.AppUser).WithMany()
               .HasForeignKey(a => a.AppUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
