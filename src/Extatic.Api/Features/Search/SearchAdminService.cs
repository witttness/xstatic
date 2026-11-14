using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Extatic.Api.Features.Search;

/// <summary>Dashboard operations: settings, collection configs, reindex jobs, overview, analytics.</summary>
public class SearchAdminService(AppDbContext db, IConfiguration? configuration = null)
{
    /// <summary>Platform ceiling for MaxDocuments (Search:MaxDocumentsCeiling, default 10 000); tenants may only lower it.</summary>
    public int MaxDocumentsCeiling =>
        Math.Clamp(configuration?.GetValue<int?>("Search:MaxDocumentsCeiling") ?? SearchSettings.DefaultMaxDocuments,
            1, SearchLimits.MaxDocumentsCeiling);

    public Task<SearchSettings> GetSettingsAsync(Guid appId, CancellationToken ct = default)
        => SearchSettingsStore.GetAsync(db, appId, ct);

    public async Task<SearchSettings> UpdateSettingsAsync(Guid appId, UpdateSearchSettingsRequest request,
        CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        if (request.DefaultLanguage is not null && !SearchLanguages.IsValid(request.DefaultLanguage))
            errors.Add(new ValidationError("/default_language", "unsupported language"));
        if (request.MaxDocuments is { } max && (max < 1 || max > MaxDocumentsCeiling))
            errors.Add(new ValidationError("/max_documents", $"max_documents must be between 1 and {MaxDocumentsCeiling}"));
        if (errors.Count > 0) throw new ValidationException("Invalid search settings", errors);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var settings = await SearchSettingsStore.LockAsync(db, appId, ct);
        var wasEnabled = settings.Enabled;
        if (request.Enabled is { } enabled) settings.Enabled = enabled;
        if (request.DefaultLanguage is not null) settings.DefaultLanguage = request.DefaultLanguage;
        if (request.MaxDocuments is { } maxDocuments) settings.MaxDocuments = maxDocuments;
        if (request.PublicSearchEnabled is { } publicSearch) settings.PublicSearchEnabled = publicSearch;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Items changed while search was off were not indexed: catch up.
        if (!wasEnabled && settings.Enabled
            && await db.SearchCollectionConfigs.AnyAsync(c => c.AppId == appId && c.Enabled, ct))
            await EnqueueReindexAsync(appId, null, ct);
        return settings;
    }

    // ---- Collection configs ----

    public async Task<List<CollectionConfigDto>> ListConfigsAsync(Guid appId, CancellationToken ct = default)
    {
        var configs = await db.SearchCollectionConfigs.AsNoTracking()
            .Where(c => c.AppId == appId)
            .Include(c => c.Collection)
            .OrderBy(c => c.Collection.Slug)
            .ToListAsync(ct);
        var counts = await DocumentCountsAsync(appId, ct);
        return configs.Select(c => ToDto(c, c.Collection, counts.GetValueOrDefault(c.CollectionId))).ToList();
    }

    public async Task<CollectionConfigDto> GetConfigAsync(Guid appId, string collectionSlug, CancellationToken ct = default)
    {
        var collection = await FindCollectionAsync(appId, collectionSlug, ct);
        var config = await db.SearchCollectionConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AppId == appId && c.CollectionId == collection.Id, ct)
            ?? throw new NotFoundException($"Collection '{collectionSlug}' has no search configuration");
        var counts = await DocumentCountsAsync(appId, ct);
        return ToDto(config, collection, counts.GetValueOrDefault(collection.Id));
    }

    public async Task<CollectionConfigSaveResult> PutConfigAsync(Guid appId, string collectionSlug,
        PutCollectionConfigRequest request, CancellationToken ct = default)
    {
        var collection = await FindCollectionAsync(appId, collectionSlug, ct);
        var settings = await SearchSettingsStore.GetAsync(db, appId, ct);
        var config = ValidateConfig(request, settings.DefaultLanguage);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO search_collection_configs (app_id, collection_id, enabled, title_pointer, body_pointers,
                    tags_pointer, url_template, section, language, created_at, updated_at)
                VALUES (@app, @collection, @enabled, @title, @body, @tags, @url, @section, @language, now(), now())
                ON CONFLICT (collection_id) DO UPDATE SET enabled = EXCLUDED.enabled,
                    title_pointer = EXCLUDED.title_pointer, body_pointers = EXCLUDED.body_pointers,
                    tags_pointer = EXCLUDED.tags_pointer, url_template = EXCLUDED.url_template,
                    section = EXCLUDED.section, language = EXCLUDED.language, updated_at = now()
                WHERE search_collection_configs.app_id = EXCLUDED.app_id
                """,
                [
                    SearchSql.P("app", appId, NpgsqlDbType.Uuid),
                    SearchSql.P("collection", collection.Id, NpgsqlDbType.Uuid),
                    SearchSql.P("enabled", config.Enabled, NpgsqlDbType.Boolean),
                    SearchSql.P("title", config.TitlePointer, NpgsqlDbType.Text),
                    SearchSql.P("body", config.BodyPointers, NpgsqlDbType.Array | NpgsqlDbType.Text),
                    SearchSql.P("tags", config.TagsPointer, NpgsqlDbType.Text),
                    SearchSql.P("url", config.UrlTemplate, NpgsqlDbType.Text),
                    SearchSql.P("section", config.Section, NpgsqlDbType.Text),
                    SearchSql.P("language", config.Language, NpgsqlDbType.Text),
                ], ct);
            if (!config.Enabled)
                await SearchIndexWriter.DeleteCollectionDocumentsAsync(db, appId, collection.Id, ct);
            await transaction.CommitAsync(ct);
        }

        ReindexJobDto? job = null;
        if (config.Enabled && settings.Enabled)
            job = (await EnqueueReindexAsync(appId, collection.Id, ct)).ToDto();
        return new CollectionConfigSaveResult(await GetConfigAsync(appId, collectionSlug, ct), job);
    }

    public async Task DeleteConfigAsync(Guid appId, string collectionSlug, CancellationToken ct = default)
    {
        var collection = await FindCollectionAsync(appId, collectionSlug, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var deleted = await db.SearchCollectionConfigs
            .Where(c => c.AppId == appId && c.CollectionId == collection.Id)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException($"Collection '{collectionSlug}' has no search configuration");
        await SearchIndexWriter.DeleteCollectionDocumentsAsync(db, appId, collection.Id, ct);
        await transaction.CommitAsync(ct);
    }

    internal static SearchCollectionConfig ValidateConfig(PutCollectionConfigRequest request, string defaultLanguage)
    {
        var errors = new List<ValidationError>();
        var title = request.TitlePointer ?? "/title";
        if (!ItemTextExtractor.IsValidPointer(title))
            errors.Add(new ValidationError("/title_pointer", "title_pointer must be a JSON pointer such as /title"));
        var body = request.BodyPointers?.Where(p => p is not null).ToArray() ?? [];
        if (body.Length > SearchLimits.MaxBodyPointers)
            errors.Add(new ValidationError("/body_pointers", $"at most {SearchLimits.MaxBodyPointers} body pointers"));
        for (var i = 0; i < body.Length; i++)
            if (!ItemTextExtractor.IsValidPointer(body[i]))
                errors.Add(new ValidationError($"/body_pointers/{i}", "must be a JSON pointer such as /body"));
        var tags = string.IsNullOrEmpty(request.TagsPointer) ? null : request.TagsPointer;
        if (tags is not null && !ItemTextExtractor.IsValidPointer(tags))
            errors.Add(new ValidationError("/tags_pointer", "tags_pointer must be a JSON pointer such as /tags"));
        var url = string.IsNullOrWhiteSpace(request.UrlTemplate) ? null : request.UrlTemplate.Trim();
        if (!ItemTextExtractor.IsValidUrlTemplate(url))
            errors.Add(new ValidationError("/url_template",
                "url_template must be a path starting with '/' or an http(s) URL, with {field} or {id} placeholders"));
        var section = string.IsNullOrWhiteSpace(request.Section) ? null : SearchText.Clean(request.Section);
        if (section is { Length: > SearchLimits.MaxSectionLength })
            errors.Add(new ValidationError("/section", $"section must be at most {SearchLimits.MaxSectionLength} characters"));
        var language = string.IsNullOrWhiteSpace(request.Language) ? defaultLanguage : request.Language.Trim().ToLowerInvariant();
        if (!SearchLanguages.IsValid(language))
            errors.Add(new ValidationError("/language", "unsupported language"));
        if (errors.Count > 0) throw new ValidationException("Invalid search configuration", errors);
        return new SearchCollectionConfig
        {
            Enabled = request.Enabled,
            TitlePointer = title,
            BodyPointers = body,
            TagsPointer = tags,
            UrlTemplate = url,
            Section = section,
            Language = language
        };
    }

    private async Task<Collection> FindCollectionAsync(Guid appId, string slug, CancellationToken ct)
        => await db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.AppId == appId && c.Slug == slug, ct)
           ?? throw new NotFoundException($"Collection '{slug}' not found");

    private async Task<Dictionary<Guid, int>> DocumentCountsAsync(Guid appId, CancellationToken ct)
        => await db.SearchDocuments
            .Where(d => d.AppId == appId && d.Source == SearchSources.Item && d.CollectionId != null)
            .GroupBy(d => d.CollectionId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    private static CollectionConfigDto ToDto(SearchCollectionConfig c, Collection collection, int documents) =>
        new(c.CollectionId, collection.Slug, collection.Name, c.Enabled, c.TitlePointer, c.BodyPointers,
            c.TagsPointer, c.UrlTemplate, c.Section, c.Language, documents, c.UpdatedAt);

    // ---- Reindex jobs ----

    /// <summary>
    /// Queues a reindex of one collection (or all, when null). At most one pending job exists per
    /// (app, collection|all) — enforced by a partial unique index — so triggering twice returns the
    /// same pending job. A job already running is not reused: changes made after it started still get
    /// a fresh pass.
    /// </summary>
    public async Task<SearchReindexJob> EnqueueReindexAsync(Guid appId, Guid? collectionId, CancellationToken ct = default)
    {
        if (collectionId is { } id && !await db.Collections.AnyAsync(c => c.AppId == appId && c.Id == id, ct))
            throw new NotFoundException("Collection not found");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var insertedId = await SearchSql.RunAsync(db, async (connection, transaction) =>
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO search_reindex_jobs (app_id, collection_id, status, processed, total, attempts,
                        created_at, updated_at)
                    VALUES (@app, @collection, 'pending', 0, 0, 0, now(), now())
                    ON CONFLICT DO NOTHING
                    RETURNING id
                    """, connection, transaction);
                command.Parameters.Add(SearchSql.P("app", appId, NpgsqlDbType.Uuid));
                command.Parameters.Add(SearchSql.P("collection", collectionId, NpgsqlDbType.Uuid));
                return await command.ExecuteScalarAsync(ct) as Guid?;
            }, ct);
            var job = await db.SearchReindexJobs.AsNoTracking()
                .Where(j => j.AppId == appId && j.CollectionId == collectionId
                    && (insertedId == null ? j.Status == ReindexStatuses.Pending : j.Id == insertedId))
                .FirstOrDefaultAsync(ct);
            if (job is not null) return job;
            // The pending job we collided with was claimed in between: try again.
        }
        throw new ConflictException("Could not queue the reindex, try again");
    }

    public async Task<SearchReindexJob> GetJobAsync(Guid appId, Guid jobId, CancellationToken ct = default)
        => await db.SearchReindexJobs.AsNoTracking().FirstOrDefaultAsync(j => j.AppId == appId && j.Id == jobId, ct)
           ?? throw new NotFoundException("Reindex job not found");

    public Task<List<SearchReindexJob>> ListJobsAsync(Guid appId, CancellationToken ct = default)
        => db.SearchReindexJobs.AsNoTracking().Where(j => j.AppId == appId)
            .OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(ct);

    // ---- Overview & analytics ----

    public async Task<SearchOverviewDto> GetOverviewAsync(App app, CancellationToken ct = default)
    {
        var stats = await new SearchDocumentService(db).GetStatsAsync(app.Id, ct);
        var settings = await SearchSettingsStore.GetAsync(db, app.Id, ct);
        var enabledCollections = await db.SearchCollectionConfigs.CountAsync(c => c.AppId == app.Id && c.Enabled, ct);
        var latest = await db.SearchReindexJobs.AsNoTracking().Where(j => j.AppId == app.Id)
            .OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        return new SearchOverviewDto(settings.Enabled, settings.PublicSearchEnabled, app.PublicId, stats.Total,
            stats.Pages, stats.Items, settings.MaxDocuments, stats.LastIndexedAt, enabledCollections, latest?.ToDto());
    }

    public async Task<SearchAnalyticsDto> GetAnalyticsAsync(Guid appId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var logs = db.SearchQueryLogs.Where(l => l.AppId == appId && l.CreatedAt >= since);
        var total = await logs.CountAsync(ct);
        var top = await Aggregate(logs, ct);
        var zero = await Aggregate(logs.Where(l => l.ResultCount == 0), ct);
        return new SearchAnalyticsDto(total, top, zero);
    }

    private static async Task<List<QueryStatDto>> Aggregate(IQueryable<SearchQueryLog> logs, CancellationToken ct)
    {
        var rows = await logs.GroupBy(l => l.Query)
            .Select(g => new
            {
                Query = g.Key,
                Count = g.Count(),
                Average = g.Average(l => (double)l.ResultCount),
                Last = g.Max(l => l.CreatedAt)
            })
            .OrderByDescending(s => s.Count).ThenByDescending(s => s.Last)
            .Take(20)
            .ToListAsync(ct);
        return rows.Select(r => new QueryStatDto(r.Query, r.Count, Math.Round(r.Average, 2), r.Last)).ToList();
    }
}
