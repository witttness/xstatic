using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Search;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extatic.Api.Tests.Features.Search;

internal static class SearchTestHelper
{
    public static async Task<(User Owner, App App, Collection Collection)> SeedAppAsync(ApiTestFixture fixture,
        bool enabled = true, int? maxDocuments = null)
    {
        var seeded = await fixture.SeedAppAsync();
        if (enabled || maxDocuments is not null)
        {
            await using var db = fixture.CreateContext();
            await new SearchAdminService(db).UpdateSettingsAsync(seeded.App.Id,
                new UpdateSearchSettingsRequest(enabled, null, maxDocuments, null));
        }
        return seeded;
    }

    public static async Task<string> SetSecretKeyAsync(ApiTestFixture fixture, Guid appId)
    {
        var key = "exk_" + Tokens.NewUrlToken();
        await using var db = fixture.CreateContext();
        await db.Apps.Where(a => a.Id == appId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ApiKeyHash, Tokens.Hash(key)));
        return key;
    }

    public static PushDocument Page(string id, string title, string body = "", string? section = null,
        string[]? tags = null, string? url = null, DateTime? publishedAt = null, string? language = null,
        string? summary = null, object? metadata = null)
        => new(id, url ?? "/" + id, title, body, summary, tags, section, language,
            metadata is null ? null : JsonSerializer.SerializeToElement(metadata), publishedAt);

    public static async Task<PushDocumentsResponse> PushAsync(ApiTestFixture fixture, Guid appId,
        params PushDocument[] documents)
    {
        await using var db = fixture.CreateContext();
        return await new SearchDocumentService(db).UpsertPagesAsync(appId, new PushDocumentsRequest([.. documents]));
    }

    public static async Task<SearchResponse> SearchAsync(ApiTestFixture fixture, Guid appId, string? q,
        int page = 1, int pageSize = 10, string? section = null, string? tags = null, string? source = null,
        string? collection = null, bool log = false)
    {
        await using var db = fixture.CreateContext();
        return await new SearchQueryService(db).SearchAsync(appId,
            new SearchQuery(q, page, pageSize, section, tags, source, collection), log);
    }

    public static async Task<List<string>> TitlesAsync(ApiTestFixture fixture, Guid appId, string q,
        string? section = null, string? tags = null, string? source = null, string? collection = null)
        => (await SearchAsync(fixture, appId, q, pageSize: 50, section: section, tags: tags, source: source,
            collection: collection)).Items.Select(i => i.Title).ToList();

    public static ItemService ItemService(ApiTestFixture fixture, AppDbContext db, params IItemChangeListener[] extra)
        => new(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture),
            [new SearchItemChangeListener(), .. extra]);

    public static SearchReindexWorker Worker(ApiTestFixture fixture)
        => new(fixture.CreateScopeFactory(), NullLogger<SearchReindexWorker>.Instance);

    public static async Task<CollectionConfigSaveResult> ConfigureCollectionAsync(ApiTestFixture fixture, Guid appId,
        string slug, bool enabled = true, string titlePointer = "/title", string[]? bodyPointers = null,
        string? tagsPointer = null, string? urlTemplate = null, string? section = null, string? language = null)
    {
        await using var db = fixture.CreateContext();
        return await new SearchAdminService(db).PutConfigAsync(appId, slug,
            new PutCollectionConfigRequest(enabled, titlePointer, bodyPointers ?? ["/body"], tagsPointer, urlTemplate,
                section, language));
    }

    public static JsonElement JsonOf(object value) => JsonSerializer.SerializeToElement(value);

    public static async Task<List<SearchDocument>> DocumentsAsync(ApiTestFixture fixture, Guid appId)
    {
        await using var db = fixture.CreateContext();
        return await db.SearchDocuments.AsNoTracking().Where(d => d.AppId == appId).OrderBy(d => d.Title).ToListAsync();
    }
}
