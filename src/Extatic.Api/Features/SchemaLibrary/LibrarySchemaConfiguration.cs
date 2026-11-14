using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Extatic.Api.Features.SchemaLibrary;

public class LibrarySchemaConfiguration : IEntityTypeConfiguration<LibrarySchema>
{
    public const string GlobalSlugIndex = "ix_library_schemas_global_slug";
    public const string AppSlugIndex = "ix_library_schemas_app_slug";

    public void Configure(EntityTypeBuilder<LibrarySchema> builder)
    {
        builder.ToTable("library_schemas", t => t.HasCheckConstraint(
            "ck_library_schemas_scope",
            "(\"Scope\" = 'Global' AND \"AppId\" IS NULL AND \"Visibility\" IS NOT NULL) OR " +
            "(\"Scope\" = 'App' AND \"AppId\" IS NOT NULL AND \"Visibility\" IS NULL)"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.Visibility).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(LibrarySchemaRules.MaxNameLength);
        builder.Property(s => s.Slug).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(LibrarySchemaRules.MaxDescriptionLength);
        builder.Property(s => s.Category).HasMaxLength(LibrarySchemaRules.MaxCategoryLength);
        builder.Property(s => s.Tags).HasColumnType("text[]");
        builder.Property(s => s.Schema).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(s => s.Slug).IsUnique().HasFilter("\"AppId\" IS NULL").HasDatabaseName(GlobalSlugIndex);
        builder.HasIndex(s => new { s.AppId, s.Slug }).IsUnique().HasFilter("\"AppId\" IS NOT NULL")
               .HasDatabaseName(AppSlugIndex);

        builder.HasOne(s => s.App)
               .WithMany()
               .HasForeignKey(s => s.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LibrarySchemaVersionConfiguration : IEntityTypeConfiguration<LibrarySchemaVersion>
{
    public void Configure(EntityTypeBuilder<LibrarySchemaVersion> builder)
    {
        builder.ToTable("library_schema_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.Schema).HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.ChangeNote).HasMaxLength(LibrarySchemaRules.MaxChangeNoteLength);
        builder.HasIndex(v => new { v.LibrarySchemaId, v.Version }).IsUnique();
        builder.HasIndex(v => v.AppId);
        builder.HasOne(v => v.LibrarySchema)
               .WithMany(s => s.Versions)
               .HasForeignKey(v => v.LibrarySchemaId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Extatic.Api.Domain.Entities.App>()
               .WithMany()
               .HasForeignKey(v => v.AppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
