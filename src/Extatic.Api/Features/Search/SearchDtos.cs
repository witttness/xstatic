using System.Text.Json;

namespace Extatic.Api.Features.Search;

// ---- Querying ----

public record SearchQuery(
    string? Q,
    int Page = 1,
    int PageSize = 10,
    string? Section = null,
    string? Tags = null,
    string? Source = null,
    string? Collection = null);

public record SearchHitDto(
    string Id,
    string Source,
    string? Url,
    string Title,
    string? Summary,
    /// <summary>HTML-escaped text; the only markup is &lt;mark&gt;…&lt;/mark&gt;.</summary>
    string Snippet,
    string? Section,
    string[] Tags,
    JsonElement Metadata,
    DateTime? PublishedAt,
    string? Collection,
    Guid? ItemId,
    double Score);

public record SearchResponse(string Query, List<SearchHitDto> Items, int Total, int Page, int PageSize);

public record SuggestionDto(string Title, string? Url);

public record SuggestResponse(List<SuggestionDto> Suggestions);

// ---- CI push (server key) ----

public record PushDocument(
    string? Id,
    string? Url,
    string? Title,
    string? Body,
    string? Summary,
    string[]? Tags,
    string? Section,
    string? Language,
    JsonElement? Metadata,
    DateTime? PublishedAt);

public record PushDocumentsRequest(List<PushDocument>? Documents);

public record PushDocumentsResponse(int Inserted, int Updated, int Unchanged, int TotalPageDocuments);

public record ExternalIdsRequest(List<string>? ExternalIds);

public record DeleteDocumentsResponse(int Deleted);

public record SyncResponse(int Deleted, int Remaining);

public record SearchStatsDto(
    bool Enabled,
    int Total,
    int Pages,
    int Items,
    int MaxDocuments,
    DateTime? LastIndexedAt);

// ---- Platform ----

public record SearchSettingsDto(
    bool Enabled,
    string DefaultLanguage,
    int MaxDocuments,
    bool PublicSearchEnabled,
    IReadOnlyList<string> AvailableLanguages);

public record UpdateSearchSettingsRequest(
    bool? Enabled,
    string? DefaultLanguage,
    int? MaxDocuments,
    bool? PublicSearchEnabled);

public record CollectionConfigDto(
    Guid CollectionId,
    string CollectionSlug,
    string CollectionName,
    bool Enabled,
    string TitlePointer,
    string[] BodyPointers,
    string? TagsPointer,
    string? UrlTemplate,
    string? Section,
    string Language,
    int DocumentCount,
    DateTime UpdatedAt);

public record PutCollectionConfigRequest(
    bool Enabled,
    string? TitlePointer,
    string[]? BodyPointers,
    string? TagsPointer,
    string? UrlTemplate,
    string? Section,
    string? Language);

public record ReindexJobDto(
    Guid Id,
    Guid? CollectionId,
    string Status,
    int Processed,
    int Total,
    string? Error,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public record CollectionConfigSaveResult(CollectionConfigDto Config, ReindexJobDto? ReindexJob);

public record SearchOverviewDto(
    bool Enabled,
    bool PublicSearchEnabled,
    string PublicId,
    int Total,
    int Pages,
    int Items,
    int MaxDocuments,
    DateTime? LastIndexedAt,
    int EnabledCollections,
    ReindexJobDto? LatestJob);

public record SearchDocumentSummaryDto(
    Guid Id,
    string Source,
    string ExternalId,
    string Title,
    string? Url,
    string? Section,
    string[] Tags,
    string Language,
    string? Collection,
    Guid? ItemId,
    int BodyLength,
    DateTime? PublishedAt,
    DateTime IndexedAt);

public record QueryStatDto(string Query, int Count, double AverageResults, DateTime LastSearchedAt);

public record SearchAnalyticsDto(int TotalQueries, List<QueryStatDto> TopQueries, List<QueryStatDto> ZeroResultQueries);

public static class SearchDtoMappers
{
    public static ReindexJobDto ToDto(this SearchReindexJob j) =>
        new(j.Id, j.CollectionId, j.Status, j.Processed, j.Total, j.Error, j.CreatedAt, j.StartedAt, j.CompletedAt);

    public static SearchSettingsDto ToDto(this SearchSettings s) =>
        new(s.Enabled, s.DefaultLanguage, s.MaxDocuments, s.PublicSearchEnabled, SearchLanguages.All);
}
