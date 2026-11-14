using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.Authentication;

public class AuthSettingsConfiguration : IEntityTypeConfiguration<AuthSettings>
{
    public void Configure(EntityTypeBuilder<AuthSettings> builder)
    {
        builder.ToTable("auth_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.HasIndex(s => s.AppId).IsUnique();
        builder.Property(s => s.VerifyUrlTemplate).HasMaxLength(2048);
        builder.Property(s => s.ResetUrlTemplate).HasMaxLength(2048);
        builder.Property(s => s.AllowedEmailDomains).HasColumnType("text[]");
        builder.HasOne(s => s.App).WithMany().HasForeignKey(s => s.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppUserCredentialConfiguration : IEntityTypeConfiguration<AppUserCredential>
{
    public void Configure(EntityTypeBuilder<AppUserCredential> builder)
    {
        builder.ToTable("app_user_credentials");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PasswordHash).IsRequired().HasMaxLength(512);
        builder.HasIndex(c => c.AppUserId).IsUnique();
        builder.HasIndex(c => c.AppId);
        builder.HasOne(c => c.App).WithMany().HasForeignKey(c => c.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.AppUser).WithMany().HasForeignKey(c => c.AppUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppUserAuthTokenConfiguration : IEntityTypeConfiguration<AppUserAuthToken>
{
    public void Configure(EntityTypeBuilder<AppUserAuthToken> builder)
    {
        builder.ToTable("app_user_auth_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Purpose).IsRequired().HasMaxLength(32);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.IpHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.AppId, t.AppUserId, t.Purpose });
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.ExpiresAt);
        builder.HasOne(t => t.App).WithMany().HasForeignKey(t => t.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.AppUser).WithMany().HasForeignKey(t => t.AppUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
