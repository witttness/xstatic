using System.Text;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Extatic.Api.Features.Search;

/// <summary>
/// Full-text queries over one app's documents. Every statement filters <c>app_id = @app</c> on
/// <c>search_documents</c> first; user text only ever reaches Postgres as a bound parameter, parsed
/// by <c>websearch_to_tsquery</c> (never raises on syntax) or as a prefix tsquery built from
/// letters/digits only (<see cref="SearchText.BuildPrefixQuery"/>).
/// </summary>
public class SearchQueryService(AppDbContext db)
{
    private static readonly string HeadlineOptions =
        $"StartSel=\"{SearchText.MarkStart}\", StopSel=\"{SearchText.MarkEnd}\", MaxWords=35, MinWords=15, " +
        "ShortWord=3, MaxFragments=2, FragmentDelimiter=\" … \"";

    // One tsquery per language present in this app's index, so each row is matched with its own stemming.
    private const string LanguagesCte = """
        q AS (
            SELECT l.language, websearch_to_tsquery(l.language::regconfig, @q) AS query
            FROM (SELECT DISTINCT language FROM search_documents WHERE app_id = @app) l
        )
        """;

    public static string ValidateQueryText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new BadRequestException("Query parameter q is required");
        var cleaned = SearchText.Clean(raw);
        if (cleaned.Length == 0)
            throw new BadRequestException("Query parameter q is required");
        if (cleaned.Length > SearchLimits.MaxQueryLength)
            throw new BadRequestException($"q must be at most {SearchLimits.MaxQueryLength} characters");
        return cleaned;
    }

    public async Task<SearchResponse> SearchAsync(Guid appId, SearchQuery query, bool logQuery,
        CancellationToken ct = default)
    {
        var q = ValidateQueryText(query.Q);
        if (query.Page < 1 || query.Page > SearchLimits.MaxPage)
            throw new BadRequestException($"page must be between 1 and {SearchLimits.MaxPage}");
        if (query.PageSize < 1 || query.PageSize > SearchLimits.MaxPageSize)
            throw new BadRequestException($"page_size must be between 1 and {SearchLimits.MaxPageSize}");
        var source = string.IsNullOrWhiteSpace(query.Source) ? null : query.Source.Trim().ToLowerInvariant();
        if (source is not null && !SearchSources.IsValid(source))
            throw new BadRequestException("source must be 'page' or 'item'");
        var section = string.IsNullOrWhiteSpace(query.Section) ? null : SearchText.Clean(query.Section);
        var tags = SearchText.NormalizeTags(query.Tags?.Split(','));

        Guid? collectionId = null;
        if (!string.IsNullOrWhiteSpace(query.Collection))
        {
            collectionId = await db.Collections
                .Where(c => c.AppId == appId && c.Slug == query.Collection)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (collectionId is null)
            {
                await LogAsync(appId, q, 0, logQuery && query.Page == 1, ct);
                return new SearchResponse(q, [], 0, query.Page, query.PageSize);
            }
        }

        var filters = new StringBuilder();
        var parameters = new List<NpgsqlParameter>
        {
            SearchSql.P("app", appId, NpgsqlDbType.Uuid),
            SearchSql.P("q", q, NpgsqlDbType.Text),
        };
        if (section is not null)
        {
            filters.Append(" AND d.section = @section");
            parameters.Add(SearchSql.P("section", section, NpgsqlDbType.Text));
        }
        if (source is not null)
        {
            filters.Append(" AND d.source = @source");
            parameters.Add(SearchSql.P("source", source, NpgsqlDbType.Text));
        }
        if (tags.Length > 0)
        {
            filters.Append(" AND d.tags @> @tags");
            parameters.Add(SearchSql.P("tags", tags, NpgsqlDbType.Array | NpgsqlDbType.Text));
        }
        if (collectionId is not null)
        {
            filters.Append(" AND d.collection_id = @collection");
            parameters.Add(SearchSql.P("collection", collectionId, NpgsqlDbType.Uuid));
        }

        var hitsCte = $"""
            WITH {LanguagesCte},
            hits AS (
                SELECT d.id, q.query, coalesce(d.published_at, d.created_at) AS recency,
                    ts_rank_cd('{"{"}0.1, 0.2, 0.4, 1.0{"}"}', d.search_vector, q.query)
                    * (1 + 0.1 / (1 + greatest(extract(epoch FROM now() - coalesce(d.published_at, d.created_at)), 0)
                        / 2592000.0)) AS score
                FROM search_documents d JOIN q ON q.language = d.language
                WHERE d.app_id = @app AND d.search_vector @@ q.query{filters}
            )
            """;
        var pageSql = hitsCte + """
            , page AS (
                SELECT h.*, count(*) OVER () AS total FROM hits h
                ORDER BY h.score DESC, h.recency DESC, h.id
                LIMIT @take OFFSET @skip
            )
            SELECT d.external_id, d.source, d.url, d.title, d.summary, d.section, d.tags, d.metadata::text,
                d.published_at, c."Slug", d.item_id, p.score::float8, p.total,
                ts_headline(d.language::regconfig, d.body, p.query, @opts)
            FROM page p
            JOIN search_documents d ON d.id = p.id AND d.app_id = @app
            LEFT JOIN collections c ON c."Id" = d.collection_id AND c."AppId" = @app
            ORDER BY p.score DESC, p.recency DESC, p.id
            """;

        var skip = (query.Page - 1) * query.PageSize;
        var (hits, total) = await SearchSql.RunAsync(db, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(pageSql, connection, transaction);
            foreach (var p in parameters) command.Parameters.Add(p.Clone());
            command.Parameters.Add(SearchSql.P("take", query.PageSize, NpgsqlDbType.Integer));
            command.Parameters.Add(SearchSql.P("skip", skip, NpgsqlDbType.Integer));
            command.Parameters.Add(SearchSql.P("opts", HeadlineOptions, NpgsqlDbType.Text));
            var list = new List<SearchHitDto>();
            long count = 0;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    count = reader.GetInt64(12);
                    list.Add(new SearchHitDto(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        SearchText.RenderSnippet(reader.IsDBNull(13) ? null : reader.GetString(13)),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetFieldValue<string[]>(6),
                        ParseMetadata(reader.GetString(7)),
                        reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetGuid(10),
                        Math.Round(reader.GetDouble(11), 6)));
                }
            }
            if (list.Count == 0 && skip > 0)
            {
                await using var countCommand = new NpgsqlCommand(hitsCte + " SELECT count(*) FROM hits",
                    connection, transaction);
                foreach (var p in parameters) countCommand.Parameters.Add(p.Clone());
                count = (long)(await countCommand.ExecuteScalarAsync(ct))!;
            }
            return (list, (int)count);
        }, ct);

        await LogAsync(appId, q, total, logQuery && query.Page == 1, ct);
        return new SearchResponse(q, hits, total, query.Page, query.PageSize);
    }

    public async Task<SuggestResponse> SuggestAsync(Guid appId, string? raw, CancellationToken ct = default)
    {
        var text = ValidateQueryText(raw);
        var tsquery = SearchText.BuildPrefixQuery(text);
        if (tsquery is null) return new SuggestResponse([]);

        // Unstemmed title words (generated title_vector, GIN-indexed) so "gett" completes "Getting started".
        var sql = $"""
            WITH m AS (
                SELECT DISTINCT ON (lower(d.title)) d.title, d.url, ts_rank_cd(d.title_vector, query) AS rank
                FROM search_documents d, to_tsquery('simple', @q) query
                WHERE d.app_id = @app AND d.title <> '' AND d.title_vector @@ query
                ORDER BY lower(d.title), rank DESC
            )
            SELECT title, url FROM m ORDER BY rank DESC, length(title), title LIMIT {SearchLimits.MaxSuggestions}
            """;
        var suggestions = await SearchSql.RunAsync(db, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add(SearchSql.P("app", appId, NpgsqlDbType.Uuid));
            command.Parameters.Add(SearchSql.P("q", tsquery, NpgsqlDbType.Text));
            var list = new List<SuggestionDto>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(new SuggestionDto(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            return list;
        }, ct);
        return new SuggestResponse(suggestions);
    }

    private async Task LogAsync(Guid appId, string query, int resultCount, bool enabled, CancellationToken ct)
    {
        if (!enabled) return;
        db.SearchQueryLogs.Add(new SearchQueryLog
        {
            AppId = appId,
            Query = SearchText.NormalizeForLog(query),
            ResultCount = resultCount
        });
        await db.SaveChangesAsync(ct);
    }

    private static JsonElement ParseMetadata(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
