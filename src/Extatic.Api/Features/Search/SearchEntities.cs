using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using System.Text;

namespace Extatic.Api.Features.Search;

public static class SearchSources
{
    public const string Page = "page";
    public const string Item = "item";
    public static bool IsValid(string? source) => source is Page or Item;
}

public static class ReindexStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// One searchable document (a pushed page or an indexed collection Item). <see cref="SearchVector"/>
/// is NOT a generated column: Postgres requires generation expressions to be immutable, and both
/// <c>language::regconfig</c> (text→regconfig cast depends on search_path) and
/// <c>array_to_string(tags, ' ')</c> are only STABLE. It is computed in SQL on every write by
/// <see cref="SearchIndexWriter"/>, which is the only code path that inserts or updates rows.
/// </summary>
public class SearchDocument : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Source { get; set; } = SearchSources.Page;
    public string ExternalId { get; set; } = string.Empty;
    public Guid? CollectionId { get; set; }
    public Guid? ItemId { get; set; }
    public string? Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string[] Tags { get; set; } = [];
    public string? Section { get; set; }
    public string Language { get; set; } = SearchLanguages.Default;
    public string Metadata { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public DateTime IndexedAt { get; set; }
    public NpgsqlTsVector? SearchVector { get; set; }
    /// <summary>Generated: to_tsvector('simple', title) — unstemmed title words for prefix suggestions.</summary>
    public NpgsqlTsVector? TitleVector { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Collection? Collection { get; set; }
}

public class SearchCollectionConfig : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid CollectionId { get; set; }
    public bool Enabled { get; set; }
    public string TitlePointer { get; set; } = "/title";
    public string[] BodyPointers { get; set; } = [];
    public string? TagsPointer { get; set; }
    public string? UrlTemplate { get; set; }
    public string? Section { get; set; }
    public string Language { get; set; } = SearchLanguages.Default;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Collection Collection { get; set; } = null!;
}

/// <summary>Per-app settings, created lazily (absent row = defaults below).</summary>
public class SearchSettings : IAppScoped
{
    public const int DefaultMaxDocuments = 10_000;

    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public bool Enabled { get; set; }
    public string DefaultLanguage { get; set; } = SearchLanguages.Default;
    public int MaxDocuments { get; set; } = DefaultMaxDocuments;
    public bool PublicSearchEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SearchQueryLog : IAppScoped
{
    public const int MaxQueryLength = 200;

    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Query { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SearchReindexJob : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    /// <summary>Null = every collection of the app.</summary>
    public Guid? CollectionId { get; set; }
    public string Status { get; set; } = ReindexStatuses.Pending;
    public int Processed { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SearchDocumentConfiguration : IEntityTypeConfiguration<SearchDocument>
{
    public void Configure(EntityTypeBuilder<SearchDocument> builder)
    {
        builder.ToTable("search_documents");
        SnakeCaseColumns.Apply(builder);
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.Source).IsRequired().HasMaxLength(16);
        builder.Property(d => d.ExternalId).IsRequired().HasMaxLength(SearchLimits.MaxExternalIdLength);
        builder.Property(d => d.Url).HasMaxLength(SearchLimits.MaxUrlLength);
        builder.Property(d => d.Title).IsRequired();
        builder.Property(d => d.Body).IsRequired();
        builder.Property(d => d.Tags).IsRequired();
        builder.Property(d => d.Section).HasMaxLength(SearchLimits.MaxSectionLength);
        builder.Property(d => d.Language).IsRequired().HasMaxLength(32);
        builder.Property(d => d.Metadata).IsRequired().HasColumnType("jsonb");
        builder.Property(d => d.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(d => d.SearchVector).HasColumnType("tsvector")
            .IsRequired().HasDefaultValueSql("''::tsvector");

        builder.HasIndex(d => new { d.AppId, d.Source, d.ExternalId }).IsUnique();
        builder.HasIndex(d => new { d.AppId, d.Source });
        builder.HasIndex(d => new { d.AppId, d.Section });
        builder.HasIndex(d => new { d.AppId, d.Language });
        builder.HasIndex(d => d.SearchVector).HasMethod("GIN");
        // A fixed regconfig makes this expression immutable, so it can be a stored generated column.
        builder.HasGeneratedTsVectorColumn(d => d.TitleVector!, "simple", d => new { d.Title });
        builder.HasIndex(d => d.TitleVector).HasMethod("GIN");
        builder.HasIndex(d => d.CollectionId);
        builder.HasIndex(d => d.ItemId);

        builder.HasOne<App>().WithMany().HasForeignKey(d => d.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Collection).WithMany().HasForeignKey(d => d.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        // Deleting an Item removes its document in the same statement (atomic with the Item delete).
        builder.HasOne<Item>().WithMany().HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SearchCollectionConfigConfiguration : IEntityTypeConfiguration<SearchCollectionConfig>
{
    public void Configure(EntityTypeBuilder<SearchCollectionConfig> builder)
    {
        builder.ToTable("search_collection_configs");
        SnakeCaseColumns.Apply(builder);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.TitlePointer).IsRequired().HasMaxLength(SearchLimits.MaxPointerLength);
        builder.Property(c => c.BodyPointers).IsRequired();
        builder.Property(c => c.TagsPointer).HasMaxLength(SearchLimits.MaxPointerLength);
        builder.Property(c => c.UrlTemplate).HasMaxLength(SearchLimits.MaxUrlTemplateLength);
        builder.Property(c => c.Section).HasMaxLength(SearchLimits.MaxSectionLength);
        builder.Property(c => c.Language).IsRequired().HasMaxLength(32);
        builder.HasIndex(c => c.CollectionId).IsUnique();
        builder.HasIndex(c => c.AppId);
        builder.HasOne<App>().WithMany().HasForeignKey(c => c.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Collection).WithMany().HasForeignKey(c => c.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SearchSettingsConfiguration : IEntityTypeConfiguration<SearchSettings>
{
    public void Configure(EntityTypeBuilder<SearchSettings> builder)
    {
        builder.ToTable("search_settings");
        SnakeCaseColumns.Apply(builder);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DefaultLanguage).IsRequired().HasMaxLength(32);
        builder.HasIndex(s => s.AppId).IsUnique();
        builder.HasOne<App>().WithMany().HasForeignKey(s => s.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SearchQueryLogConfiguration : IEntityTypeConfiguration<SearchQueryLog>
{
    public void Configure(EntityTypeBuilder<SearchQueryLog> builder)
    {
        builder.ToTable("search_query_logs");
        SnakeCaseColumns.Apply(builder);
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.Query).IsRequired().HasMaxLength(SearchQueryLog.MaxQueryLength);
        builder.HasIndex(l => new { l.AppId, l.CreatedAt });
        builder.HasIndex(l => l.CreatedAt);
        builder.HasOne<App>().WithMany().HasForeignKey(l => l.AppId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SearchReindexJobConfiguration : IEntityTypeConfiguration<SearchReindexJob>
{
    public void Configure(EntityTypeBuilder<SearchReindexJob> builder)
    {
        builder.ToTable("search_reindex_jobs");
        SnakeCaseColumns.Apply(builder);
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(j => j.Status).IsRequired().HasMaxLength(16);
        builder.Property(j => j.Error).HasMaxLength(2000);
        // At most one *pending* job per (app, collection | all): triggering twice coalesces.
        builder.HasIndex(j => new { j.AppId, j.CollectionId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_search_reindex_jobs_one_pending");
        builder.HasIndex(j => new { j.Status, j.LeaseExpiresAt });
        builder.HasIndex(j => new { j.AppId, j.CreatedAt });
        builder.HasOne<App>().WithMany().HasForeignKey(j => j.AppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Collection>().WithMany().HasForeignKey(j => j.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Search tables use snake_case column names (unlike the core tables) because they are queried
/// with hand-written SQL (full-text functions EF cannot express).
/// </summary>
internal static class SnakeCaseColumns
{
    public static void Apply<T>(EntityTypeBuilder<T> builder) where T : class
    {
        foreach (var property in builder.Metadata.GetProperties())
            property.SetColumnName(ToSnake(property.Name));
    }

    public static string ToSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
