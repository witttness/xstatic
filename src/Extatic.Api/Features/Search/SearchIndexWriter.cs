using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Extatic.Api.Features.Search;

/// <summary>A fully normalised document ready to be written.</summary>
public sealed record IndexDocument(
    string Source,
    string ExternalId,
    Guid? CollectionId,
    Guid? ItemId,
    string? Url,
    string Title,
    string Body,
    string? Summary,
    string[] Tags,
    string? Section,
    string Language,
    string MetadataJson,
    DateTime? PublishedAt)
{
    public string ContentHash()
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            Source, ExternalId, CollectionId, ItemId, Url, Title, Body, Summary, Tags, Section, Language,
            MetadataJson, PublishedAt?.ToString("O")
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public enum UpsertOutcome { Inserted, Updated, Unchanged }

/// <summary>Raw-SQL access on the DbContext's connection, enlisted in its current transaction.</summary>
internal static class SearchSql
{
    public static async Task<T> RunAsync<T>(AppDbContext db, Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> work,
        CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
            return await work(connection, transaction);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static NpgsqlParameter P(object? value, NpgsqlDbType type)
        => new() { Value = value ?? DBNull.Value, NpgsqlDbType = type };

    public static NpgsqlParameter P(string name, object? value, NpgsqlDbType type)
        => new(name, type) { Value = value ?? DBNull.Value };
}

/// <summary>
/// The only writer of <c>search_documents</c>. Upserts by (app_id, source, external_id), computes
/// <c>search_vector</c> in the same statement (title A, summary+tags B, body C, using the row's
/// allowlisted language) and skips rows whose content hash is unchanged.
/// </summary>
public static class SearchIndexWriter
{
    private const string UpsertSql = """
        INSERT INTO search_documents AS d (app_id, source, external_id, collection_id, item_id, url, title, body,
            summary, tags, section, language, metadata, content_hash, published_at, indexed_at, created_at,
            updated_at, search_vector)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, now(), now(), now(),
            setweight(to_tsvector($12::regconfig, $7), 'A')
            || setweight(to_tsvector($12::regconfig, coalesce($9, '') || ' ' || array_to_string($10, ' ')), 'B')
            || setweight(to_tsvector($12::regconfig, $8), 'C'))
        ON CONFLICT (app_id, source, external_id) DO UPDATE SET
            collection_id = EXCLUDED.collection_id, item_id = EXCLUDED.item_id, url = EXCLUDED.url,
            title = EXCLUDED.title, body = EXCLUDED.body, summary = EXCLUDED.summary, tags = EXCLUDED.tags,
            section = EXCLUDED.section, language = EXCLUDED.language, metadata = EXCLUDED.metadata,
            content_hash = EXCLUDED.content_hash, published_at = EXCLUDED.published_at,
            search_vector = EXCLUDED.search_vector, indexed_at = now(), updated_at = now()
        WHERE d.content_hash IS DISTINCT FROM EXCLUDED.content_hash
        RETURNING (xmax = 0)
        """;

    public static async Task<List<UpsertOutcome>> UpsertAsync(AppDbContext db, Guid appId,
        IReadOnlyList<IndexDocument> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];
        foreach (var doc in documents)
            if (!SearchLanguages.IsValid(doc.Language))
                throw new ArgumentException($"Language '{doc.Language}' is not allowed");

        return await SearchSql.RunAsync(db, async (connection, transaction) =>
        {
            await using var batch = new NpgsqlBatch(connection, transaction);
            foreach (var doc in documents)
            {
                var command = new NpgsqlBatchCommand(UpsertSql);
                command.Parameters.Add(SearchSql.P(appId, NpgsqlDbType.Uuid));
                command.Parameters.Add(SearchSql.P(doc.Source, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.ExternalId, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.CollectionId, NpgsqlDbType.Uuid));
                command.Parameters.Add(SearchSql.P(doc.ItemId, NpgsqlDbType.Uuid));
                command.Parameters.Add(SearchSql.P(doc.Url, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Title, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Body, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Summary, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Tags, NpgsqlDbType.Array | NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Section, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.Language, NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.MetadataJson, NpgsqlDbType.Jsonb));
                command.Parameters.Add(SearchSql.P(doc.ContentHash(), NpgsqlDbType.Text));
                command.Parameters.Add(SearchSql.P(doc.PublishedAt, NpgsqlDbType.TimestampTz));
                batch.BatchCommands.Add(command);
            }

            var outcomes = new List<UpsertOutcome>(documents.Count);
            await using var reader = await batch.ExecuteReaderAsync(ct);
            do
            {
                outcomes.Add(await reader.ReadAsync(ct)
                    ? reader.GetBoolean(0) ? UpsertOutcome.Inserted : UpsertOutcome.Updated
                    : UpsertOutcome.Unchanged);
            } while (await reader.NextResultAsync(ct));
            return outcomes;
        }, ct);
    }

    /// <summary>Removes every Item document of a collection (config disabled or deleted).</summary>
    public static Task<int> DeleteCollectionDocumentsAsync(AppDbContext db, Guid appId, Guid collectionId,
        CancellationToken ct = default)
        => db.SearchDocuments
            .Where(d => d.AppId == appId && d.Source == SearchSources.Item && d.CollectionId == collectionId)
            .ExecuteDeleteAsync(ct);

    public static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        { } v => v.ToUniversalTime()
    };
}

/// <summary>Builds the IndexDocument for an Item according to its collection's config.</summary>
public static class ItemDocumentBuilder
{
    public static IndexDocument Build(SearchCollectionConfig config, Guid itemId, string data, DateTime createdAt)
    {
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        var title = SearchText.Truncate(SearchText.Clean(ItemTextExtractor.Text(root, config.TitlePointer)),
            SearchLimits.MaxTitleLength);
        var body = string.Join(" ", config.BodyPointers.Select(p => ItemTextExtractor.Text(root, p)));
        body = SearchText.TruncateUtf8(SearchText.StripHtml(body), SearchLimits.MaxBodyBytes);
        var tags = config.TagsPointer is null
            ? []
            : SearchText.NormalizeTags(ItemTextExtractor.Strings(ItemTextExtractor.Resolve(root, config.TagsPointer)))
                .Where(t => t.Length <= SearchLimits.MaxTagLength).Take(SearchLimits.MaxTags).ToArray();
        return new IndexDocument(
            SearchSources.Item,
            itemId.ToString(),
            config.CollectionId,
            itemId,
            ItemTextExtractor.ExpandUrl(config.UrlTemplate, itemId, root),
            title,
            body,
            null,
            tags,
            config.Section,
            config.Language,
            "{}",
            SearchIndexWriter.ToUtc(createdAt));
    }
}
