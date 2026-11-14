using System.Reflection;
using Extatic.Api.Auth;
using Extatic.Api.Auth.Schemes;
using Extatic.Api.Features.Search;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Search.SearchTestHelper;

namespace Extatic.Api.Tests.Features.Search;

public class SearchPushTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task BulkUpsert_InsertsUpdatesAndSkipsUnchanged()
    {
        var (_, app, _) = await SeedAppAsync(fixture);

        var first = await PushAsync(fixture, app.Id, Page("a", "Alpha", "first"), Page("b", "Beta", "second"));
        Assert.Equal(new PushDocumentsResponse(2, 0, 0, 2), first);
        var indexedAt = (await DocumentsAsync(fixture, app.Id)).ToDictionary(d => d.ExternalId, d => d.IndexedAt);

        var second = await PushAsync(fixture, app.Id,
            Page("a", "Alpha", "first"), Page("b", "Beta", "changed body"), Page("c", "Gamma", "third"));
        Assert.Equal(new PushDocumentsResponse(1, 1, 1, 3), second);

        var docs = (await DocumentsAsync(fixture, app.Id)).ToDictionary(d => d.ExternalId);
        Assert.Equal(indexedAt["a"], docs["a"].IndexedAt);
        Assert.True(docs["b"].IndexedAt > indexedAt["b"]);
        Assert.Equal("changed body", docs["b"].Body);
        Assert.Single(await TitlesAsync(fixture, app.Id, "changed"));
        Assert.Empty(await TitlesAsync(fixture, app.Id, "second"));
    }

    [Fact]
    public async Task ExternalId_DefaultsToUrl()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, new PushDocument(null, "/about", "About", "us", null, null, null, null, null, null));
        Assert.Equal("/about", (await DocumentsAsync(fixture, app.Id)).Single().ExternalId);
    }

    [Fact]
    public async Task Sync_PrunesPagesNotListed_AndLeavesItemsAlone()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("keep", "Keep"), Page("drop1", "Drop one"), Page("drop2", "Drop two"));
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using (var db = fixture.CreateContext())
            await ItemService(fixture, db).CreateAsync(collection.Id, null, JsonOf(new { title = "Item doc" }), null, app.Id);

        await using (var db = fixture.CreateContext())
        {
            var result = await new SearchDocumentService(db).SyncPagesAsync(app.Id, new ExternalIdsRequest(["keep", "unknown"]));
            Assert.Equal(new SyncResponse(2, 1), result);
        }
        Assert.Equal(["Item doc", "Keep"], (await DocumentsAsync(fixture, app.Id)).Select(d => d.Title));
    }

    [Fact]
    public async Task Sync_WithEmptyList_IsRejected()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("keep", "Keep"));
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            new SearchDocumentService(db).SyncPagesAsync(app.Id, new ExternalIdsRequest([])));
        Assert.Single(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task Delete_RemovesOnlyListedPagesOfThisApp()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        var (_, other, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("x", "X"), Page("y", "Y"));
        await PushAsync(fixture, other.Id, Page("x", "Other X"));

        await using (var db = fixture.CreateContext())
            Assert.Equal(1, (await new SearchDocumentService(db).DeletePagesAsync(app.Id, new ExternalIdsRequest(["x"]))).Deleted);

        Assert.Equal(["Y"], (await DocumentsAsync(fixture, app.Id)).Select(d => d.Title));
        Assert.Equal(["Other X"], (await DocumentsAsync(fixture, other.Id)).Select(d => d.Title));
    }

    [Fact]
    public async Task Validation_ReportsFieldPointers()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => PushAsync(fixture, app.Id,
            Page("ok", "Fine"),
            new PushDocument(null, null, "No id", null, null, null, null, null, null, null),
            Page("bad-url", "Bad", url: "javascript:alert(1)"),
            Page("bad-lang", "Bad", language: "klingon"),
            Page("ok", "Duplicate"),
            Page("big", "Big", new string('x', SearchLimits.MaxBodyBytes + 1)),
            Page("no-title", "  "),
            new PushDocument("meta", "/m", "Meta", null, null, null, null, null, JsonOf(new[] { 1 }), null),
            Page("tags", "Tags", tags: Enumerable.Range(0, 51).Select(i => $"t{i}").ToArray())));

        var pointers = ex.Errors.Select(e => e.Pointer).ToList();
        Assert.Equal(
        [
            "/documents/1/id", "/documents/2/url", "/documents/3/language", "/documents/4/id",
            "/documents/5/body", "/documents/6/title", "/documents/7/metadata", "/documents/8/tags"
        ], pointers);
        Assert.Empty(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task PushLimits_500DocumentsPerRequest()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        var docs = Enumerable.Range(0, 501).Select(i => Page($"d{i}", $"Doc {i}")).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() => PushAsync(fixture, app.Id, docs));
        Assert.Equal(500, (await PushAsync(fixture, app.Id, docs[..500])).Inserted);
    }

    [Fact]
    public async Task Push_WhenSearchDisabled_IsConflict()
    {
        var (_, app, _) = await SeedAppAsync(fixture, enabled: false);
        await Assert.ThrowsAsync<ConflictException>(() => PushAsync(fixture, app.Id, Page("a", "A")));
    }

    [Fact]
    public async Task MaxDocuments_IsEnforced_ButUpdatesStillAllowed()
    {
        var (_, app, _) = await SeedAppAsync(fixture, maxDocuments: 2);
        await PushAsync(fixture, app.Id, Page("a", "A"), Page("b", "B"));

        await Assert.ThrowsAsync<ValidationException>(() => PushAsync(fixture, app.Id, Page("c", "C")));
        Assert.Equal(1, (await PushAsync(fixture, app.Id, Page("a", "A updated"))).Updated);
        Assert.Equal(2, (await DocumentsAsync(fixture, app.Id)).Count);
    }

    [Fact]
    public async Task MaxDocuments_HoldsUnderConcurrentPushes()
    {
        var (_, app, _) = await SeedAppAsync(fixture, maxDocuments: 5);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            try
            {
                await PushAsync(fixture, app.Id, Page($"doc-{i}", $"Doc {i}"));
                return true;
            }
            catch (ValidationException)
            {
                return false;
            }
        }));

        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(5, (await DocumentsAsync(fixture, app.Id)).Count);
    }

    [Fact]
    public async Task MaxDocuments_CannotBeRaisedAboveThePlatformCeiling()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await using var db = fixture.CreateContext();
        var service = new SearchAdminService(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(null, null, 10_001, null)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(null, "klingon", null, null)));
        Assert.Equal(500, (await service.UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(null, "german", 500, null))).MaxDocuments);
    }

    [Fact]
    public async Task Stats_CountBySource()
    {
        var (_, app, _) = await SeedAppAsync(fixture, maxDocuments: 50);
        await PushAsync(fixture, app.Id, Page("a", "A"), Page("b", "B"));
        await using var db = fixture.CreateContext();
        var stats = await new SearchDocumentService(db).GetStatsAsync(app.Id);
        Assert.Equal((true, 2, 2, 0, 50), (stats.Enabled, stats.Total, stats.Pages, stats.Items, stats.MaxDocuments));
        Assert.NotNull(stats.LastIndexedAt);
    }

    // ---- Auth ----

    private static readonly string[] ServerKeyActions =
        [nameof(ClientSearchController.Push), nameof(ClientSearchController.Delete),
         nameof(ClientSearchController.Sync), nameof(ClientSearchController.Stats)];

    [Fact]
    public void PushEndpoints_RequireTheServerKeyPolicy_AndQueriesUseTheApiKeyScheme()
    {
        foreach (var name in ServerKeyActions)
        {
            var attribute = typeof(ClientSearchController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!;
            Assert.Equal(PolicyNames.AppServerKey, attribute.Policy);
        }
        foreach (var name in new[] { nameof(ClientSearchController.Search), nameof(ClientSearchController.Suggest) })
        {
            var attribute = typeof(ClientSearchController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!;
            Assert.Equal(AuthSchemes.ApiKey, attribute.AuthenticationSchemes);
        }
        Assert.Null(typeof(ClientSearchController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task PushAuth_PublicApiKeyAloneIsRejected_SecretKeyIsAccepted()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        var (_, other, _) = await SeedAppAsync(fixture);
        var key = await SetSecretKeyAsync(fixture, app.Id);
        var otherKey = await SetSecretKeyAsync(fixture, other.Id);
        using var pipeline = new TestAuthPipeline(fixture);
        var path = $"/api/{app.PublicId}/search/documents";

        var publicOnly = pipeline.CreateContext(path);
        Assert.False((await pipeline.AuthorizeAsync(publicOnly, PolicyNames.AppServerKey)).Succeeded);

        var wrongApp = pipeline.CreateContext(path, new Dictionary<string, string>
            { [ServerKeyAuthenticationHandler.HeaderName] = otherKey });
        Assert.False((await pipeline.AuthorizeAsync(wrongApp, PolicyNames.AppServerKey)).Succeeded);

        var withKey = pipeline.CreateContext(path, new Dictionary<string, string>
            { [ServerKeyAuthenticationHandler.HeaderName] = key });
        Assert.True((await pipeline.AuthorizeAsync(withKey, PolicyNames.AppServerKey)).Succeeded);
        Assert.Equal(app.Id, ((Domain.Entities.App)withKey.Items["CurrentApp"]!).Id);
    }

    [Fact]
    public async Task ClientController_UsesCurrentAppForPush()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = new ClientSearchController(db, new SearchQueryService(db), new SearchDocumentService(db))
        {
            ControllerContext = TestContexts.Client(app)
        };
        await controller.Push(new PushDocumentsRequest([Page("via-controller", "Controller page")]));
        Assert.True(await db.SearchDocuments.AnyAsync(d => d.AppId == app.Id && d.ExternalId == "via-controller"));
    }
}
