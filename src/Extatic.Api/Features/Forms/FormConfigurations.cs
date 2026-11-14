using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Forms;

public class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.ToTable("forms");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.Name).IsRequired().HasMaxLength(FormRules.MaxNameLength);
        builder.Property(f => f.Slug).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Schema).HasColumnType("jsonb");
        builder.Property(f => f.NotificationEmails).HasColumnType("text[]");
        builder.Property(f => f.SuccessRedirectUrl).HasMaxLength(FormRules.MaxRedirectUrlLength);
        builder.Property(f => f.HoneypotField).HasMaxLength(FormRules.MaxFieldNameLength);
        builder.Property(f => f.IsActive).HasDefaultValue(true);
        builder.Property(f => f.StoreSubmissions).HasDefaultValue(true);
        builder.Property(f => f.RequireAppUser).HasDefaultValue(false);
        builder.HasIndex(f => new { f.AppId, f.Slug }).IsUnique();
        builder.HasOne(f => f.App)
               .WithMany()
               .HasForeignKey(f => f.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("form_submissions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.Data).IsRequired().HasColumnType("jsonb");
        builder.Property(s => s.IpHash).IsRequired().HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(FormSubmission.MaxUserAgentLength);
        builder.Property(s => s.Referrer).HasMaxLength(FormSubmission.MaxReferrerLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => new { s.FormId, s.CreatedAt });
        builder.HasIndex(s => s.AppId);
        builder.HasOne(s => s.App)
               .WithMany()
               .HasForeignKey(s => s.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Form)
               .WithMany(f => f.Submissions)
               .HasForeignKey(s => s.FormId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.AppUser)
               .WithMany()
               .HasForeignKey(s => s.AppUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
