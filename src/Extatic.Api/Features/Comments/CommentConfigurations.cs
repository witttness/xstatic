using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Comments;

public class CommentSettingsConfiguration : IEntityTypeConfiguration<CommentSettings>
{
    public void Configure(EntityTypeBuilder<CommentSettings> builder)
    {
        builder.ToTable("comment_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.HasIndex(s => s.AppId).IsUnique();
        builder.Property(s => s.ModerationMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.BlockedTermAction).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.BlockedTerms).HasColumnType("text[]");
        builder.Property(s => s.NotifyEmails).HasColumnType("text[]");
        builder.HasOne(s => s.App)
               .WithMany()
               .HasForeignKey(s => s.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PageKey).IsRequired().HasMaxLength(CommentRules.MaxPageKeyLength);
        builder.Property(c => c.PageUrl).HasMaxLength(CommentRules.MaxPageUrlLength);
        builder.Property(c => c.PageTitle).HasMaxLength(CommentRules.MaxPageTitleLength);
        builder.Property(c => c.AuthorName).IsRequired().HasMaxLength(CommentRules.MaxAuthorNameLength);
        builder.Property(c => c.AuthorEmail).HasMaxLength(320);
        builder.Property(c => c.Body).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.IpHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.UserAgent).HasMaxLength(CommentRules.MaxUserAgentLength);
        builder.HasIndex(c => new { c.AppId, c.PageKey, c.Status, c.CreatedAt });
        builder.HasIndex(c => new { c.AppId, c.Status, c.CreatedAt });
        builder.HasIndex(c => c.ParentId);
        builder.HasIndex(c => c.AppUserId);
        builder.HasOne(c => c.App)
               .WithMany()
               .HasForeignKey(c => c.AppId)
               .OnDelete(DeleteBehavior.Cascade);
        // Platform hard delete removes the whole sub-thread; author deletes tombstone instead.
        builder.HasOne(c => c.Parent)
               .WithMany(c => c.Replies)
               .HasForeignKey(c => c.ParentId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.AppUser)
               .WithMany()
               .HasForeignKey(c => c.AppUserId)
               .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(c => c.ModeratedBy)
               .WithMany()
               .HasForeignKey(c => c.ModeratedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CommentPageConfiguration : IEntityTypeConfiguration<CommentPage>
{
    public void Configure(EntityTypeBuilder<CommentPage> builder)
    {
        builder.ToTable("comment_pages");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.PageKey).IsRequired().HasMaxLength(CommentRules.MaxPageKeyLength);
        builder.HasIndex(p => new { p.AppId, p.PageKey }).IsUnique();
        builder.HasOne(p => p.App)
               .WithMany()
               .HasForeignKey(p => p.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
