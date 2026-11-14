using System.Text;
using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Search;

/// <summary>Lazy per-app settings; <see cref="LockAsync"/> serialises capacity-sensitive writes per app.</summary>
public static class SearchSettingsStore
{
    public static async Task<SearchSettings> GetAsync(AppDbContext db, Guid appId, CancellationToken ct = default)
        => await db.SearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AppId == appId, ct)
           ?? new SearchSettings { AppId = appId };

    public static Task EnsureAsync(AppDbContext db, Guid appId, CancellationToken ct = default)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO search_settings (app_id, enabled, default_language, max_documents, public_search_enabled,
                created_at, updated_at)
            VALUES ({appId}, FALSE, {SearchLanguages.Default}, {SearchSettings.DefaultMaxDocuments}, TRUE, now(), now())
            ON CONFLICT (app_id) DO NOTHING
            """, ct);

    /// <summary>Creates the row if needed and locks it FOR UPDATE (call inside a transaction). Tracked.</summary>
    public static async Task<SearchSettings> LockAsync(AppDbContext db, Guid appId, CancellationToken ct = default)
    {
        await EnsureAsync(db, appId, ct);
        var rows = await db.SearchSettings
            .FromSqlInterpolated($"SELECT * FROM search_settings WHERE app_id = {appId} FOR UPDATE")
            .AsTracking()
            .ToListAsync(ct);
        return rows.Single();
    }
}

/// <summary>Page documents pushed by CI, plus the dashboard's document browser.</summary>
public class SearchDocumentService(AppDbContext db)
{
    public async Task<PushDocumentsResponse> UpsertPagesAsync(Guid appId, PushDocumentsRequest request,
        CancellationToken ct = default)
    {
        var input = request.Documents
            ?? throw new ValidationException("Invalid request", [new ValidationError("/documents", "documents is required")]);
        if (input.Count == 0)
            throw new ValidationException("Invalid request", [new ValidationError("/documents", "documents must not be empty")]);
        if (input.Count > SearchLimits.MaxDocumentsPerPush)
            throw new ValidationException("Invalid request",
                [new ValidationError("/documents", $"at most {SearchLimits.MaxDocumentsPerPush} documents per request")]);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var settings = await SearchSettingsStore.LockAsync(db, appId, ct);
        if (!settings.Enabled)
            throw new ConflictException("Search is disabled for this app. Enable it in the dashboard first.");

        var documents = Validate(input, settings.DefaultLanguage);
        var ids = documents.Select(d => d.ExternalId).ToList();

        var pageQuery = db.SearchDocuments.Where(d => d.AppId == appId && d.Source == SearchSources.Page);
        var currentTotal = await pageQuery.CountAsync(ct);
        var existing = await pageQuery.CountAsync(d => ids.Contains(d.ExternalId), ct);
        var newCount = documents.Count - existing;
        if (currentTotal + newCount > settings.MaxDocuments)
            throw new ValidationException("Document limit reached",
            [
                new ValidationError("/documents",
                    $"This push would bring the index to {currentTotal + newCount} page documents; the limit is {settings.MaxDocuments}.")
            ]);

        var outcomes = await SearchIndexWriter.UpsertAsync(db, appId, documents, ct);
        await transaction.CommitAsync(ct);
        return new PushDocumentsResponse(
            outcomes.Count(o => o == UpsertOutcome.Inserted),
            outcomes.Count(o => o == UpsertOutcome.Updated),
            outcomes.Count(o => o == UpsertOutcome.Unchanged),
            currentTotal + newCount);
    }

    internal static List<IndexDocument> Validate(IReadOnlyList<PushDocument> input, string defaultLanguage)
    {
        var errors = new List<ValidationError>();
        var result = new List<IndexDocument>(input.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var doc = input[i];
            var at = $"/documents/{i}";
            if (doc is null) { errors.Add(new ValidationError(at, "document is required")); continue; }
            var before = errors.Count;

            var url = string.IsNullOrWhiteSpace(doc.Url) ? null : doc.Url.Trim();
            if (url is not null && !SearchText.IsSafeUrl(url))
                errors.Add(new ValidationError($"{at}/url", "url must be a path starting with '/' or an http(s) URL"));

            var externalId = SearchText.Clean(string.IsNullOrWhiteSpace(doc.Id) ? url : doc.Id);
            if (externalId.Length == 0)
                errors.Add(new ValidationError($"{at}/id", "id (or url) is required"));
            else if (externalId.Length > SearchLimits.MaxExternalIdLength)
                errors.Add(new ValidationError($"{at}/id", $"id must be at most {SearchLimits.MaxExternalIdLength} characters"));
            else if (!seen.Add(externalId))
                errors.Add(new ValidationError($"{at}/id", "duplicate id in this request"));

            var title = SearchText.Clean(doc.Title);
            if (title.Length == 0)
                errors.Add(new ValidationError($"{at}/title", "title is required"));
            else if (title.Length > SearchLimits.MaxTitleLength)
                errors.Add(new ValidationError($"{at}/title", $"title must be at most {SearchLimits.MaxTitleLength} characters"));

            var body = string.Empty;
            if (doc.Body is { Length: > SearchLimits.MaxRawBodyChars })
                errors.Add(new ValidationError($"{at}/body", "body is too large"));
            else
            {
                body = SearchText.StripHtml(doc.Body);
                if (Encoding.UTF8.GetByteCount(body) > SearchLimits.MaxBodyBytes)
                    errors.Add(new ValidationError($"{at}/body",
                        $"body text must be at most {SearchLimits.MaxBodyBytes / 1024} KB after HTML is stripped"));
            }

            var summary = string.IsNullOrWhiteSpace(doc.Summary) ? null : SearchText.Clean(doc.Summary);
            if (summary is { Length: > SearchLimits.MaxSummaryLength })
                errors.Add(new ValidationError($"{at}/summary", $"summary must be at most {SearchLimits.MaxSummaryLength} characters"));

            var tags = SearchText.NormalizeTags(doc.Tags);
            if (tags.Length > SearchLimits.MaxTags)
                errors.Add(new ValidationError($"{at}/tags", $"at most {SearchLimits.MaxTags} tags"));
            if (tags.Any(t => t.Length > SearchLimits.MaxTagLength))
                errors.Add(new ValidationError($"{at}/tags", $"tags must be at most {SearchLimits.MaxTagLength} characters"));

            var section = string.IsNullOrWhiteSpace(doc.Section) ? null : SearchText.Clean(doc.Section);
            if (section is { Length: > SearchLimits.MaxSectionLength })
                errors.Add(new ValidationError($"{at}/section", $"section must be at most {SearchLimits.MaxSectionLength} characters"));

            var language = string.IsNullOrWhiteSpace(doc.Language) ? defaultLanguage : doc.Language.Trim().ToLowerInvariant();
            if (!SearchLanguages.IsValid(language))
                errors.Add(new ValidationError($"{at}/language", $"language must be one of: {string.Join(", ", SearchLanguages.All)}"));

            var metadata = "{}";
            if (doc.Metadata is { } meta && meta.ValueKind != JsonValueKind.Null)
            {
                if (meta.ValueKind != JsonValueKind.Object)
                    errors.Add(new ValidationError($"{at}/metadata", "metadata must be an object"));
                else
                {
                    metadata = meta.GetRawText();
                    if (metadata.Contains("\\u0000") || Encoding.UTF8.GetByteCount(metadata) > SearchLimits.MaxMetadataBytes)
                        errors.Add(new ValidationError($"{at}/metadata",
                            $"metadata must be at most {SearchLimits.MaxMetadataBytes / 1024} KB and contain no NUL characters"));
                }
            }

            if (errors.Count == before)
                result.Add(new IndexDocument(SearchSources.Page, externalId, null, null, url, title, body, summary,
                    tags, section, language, metadata, SearchIndexWriter.ToUtc(doc.PublishedAt)));
        }
        if (errors.Count > 0)
            throw new ValidationException("Invalid documents", errors.Take(100));
        return result;
    }

    public async Task<DeleteDocumentsResponse> DeletePagesAsync(Guid appId, ExternalIdsRequest request,
        CancellationToken ct = default)
    {
        var ids = ValidateIds(request, SearchLimits.MaxIdsPerRequest);
        var deleted = await db.SearchDocuments
            .Where(d => d.AppId == appId && d.Source == SearchSources.Page && ids.Contains(d.ExternalId))
            .ExecuteDeleteAsync(ct);
        return new DeleteDocumentsResponse(deleted);
    }

    /// <summary>"These are all my current pages": deletes page documents whose id is not listed.</summary>
    public async Task<SyncResponse> SyncPagesAsync(Guid appId, ExternalIdsRequest request, CancellationToken ct = default)
    {
        var ids = ValidateIds(request, SearchLimits.MaxDocumentsCeiling);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await SearchSettingsStore.LockAsync(db, appId, ct);
        var pages = db.SearchDocuments.Where(d => d.AppId == appId && d.Source == SearchSources.Page);
        var deleted = await pages.Where(d => !ids.Contains(d.ExternalId)).ExecuteDeleteAsync(ct);
        var remaining = await pages.CountAsync(ct);
        await transaction.CommitAsync(ct);
        return new SyncResponse(deleted, remaining);
    }

    private static List<string> ValidateIds(ExternalIdsRequest request, int max)
    {
        var ids = request.ExternalIds?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(SearchText.Clean)
            .Distinct(StringComparer.Ordinal).ToList();
        if (ids is null || ids.Count == 0)
            throw new BadRequestException("external_ids must contain at least one id");
        if (ids.Count > max)
            throw new BadRequestException($"external_ids may contain at most {max} ids");
        return ids;
    }

    public async Task<SearchStatsDto> GetStatsAsync(Guid appId, CancellationToken ct = default)
    {
        var settings = await SearchSettingsStore.GetAsync(db, appId, ct);
        var bySource = await db.SearchDocuments.Where(d => d.AppId == appId)
            .GroupBy(d => d.Source)
            .Select(g => new { Source = g.Key, Count = g.Count(), Last = g.Max(d => d.IndexedAt) })
            .ToListAsync(ct);
        var pages = bySource.FirstOrDefault(s => s.Source == SearchSources.Page)?.Count ?? 0;
        var items = bySource.FirstOrDefault(s => s.Source == SearchSources.Item)?.Count ?? 0;
        DateTime? last = bySource.Count == 0 ? null : bySource.Max(s => s.Last);
        return new SearchStatsDto(settings.Enabled, pages + items, pages, items, settings.MaxDocuments, last);
    }

    public async Task<PagedResult<SearchDocumentSummaryDto>> BrowseAsync(Guid appId, int page, int pageSize,
        string? source, string? section, string? text, CancellationToken ct = default)
    {
        Paging.Validate(page, pageSize);
        var query = db.SearchDocuments.AsNoTracking().Where(d => d.AppId == appId);
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (!SearchSources.IsValid(source)) throw new BadRequestException("source must be 'page' or 'item'");
            query = query.Where(d => d.Source == source);
        }
        if (!string.IsNullOrWhiteSpace(section))
            query = query.Where(d => d.Section == section);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cleaned = SearchText.Truncate(SearchText.Clean(text), SearchLimits.MaxQueryLength);
            var pattern = "%" + cleaned.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(d => EF.Functions.ILike(d.Title, pattern, "\\")
                || EF.Functions.ILike(d.ExternalId, pattern, "\\")
                || (d.Url != null && EF.Functions.ILike(d.Url, pattern, "\\")));
        }
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(d => d.IndexedAt).ThenBy(d => d.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize)
            .Select(d => new SearchDocumentSummaryDto(d.Id, d.Source, d.ExternalId, d.Title, d.Url, d.Section,
                d.Tags, d.Language, d.Collection != null ? d.Collection.Slug : null, d.ItemId, d.Body.Length,
                d.PublishedAt, d.IndexedAt))
            .ToListAsync(ct);
        return new PagedResult<SearchDocumentSummaryDto>(rows, total, page, pageSize);
    }

    public async Task DeleteDocumentAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        var deleted = await db.SearchDocuments.Where(d => d.AppId == appId && d.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Search document not found");
    }
}
