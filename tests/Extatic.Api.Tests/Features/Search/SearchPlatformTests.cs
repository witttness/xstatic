using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Search;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Extatic.Api.Tests.Features.Search.SearchTestHelper;

namespace Extatic.Api.Tests.Features.Search;

public class SearchPlatformTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static ClientSearchController Client(AppDbContext db, App app) =>
        new(db, new SearchQueryService(db), new SearchDocumentService(db)) { ControllerContext = TestContexts.Client(app) };

    private static SearchPlatformController Platform(AppDbContext db, App app, Guid userId) =>
        new(new SearchAdminService(db), new SearchDocumentService(db), new SearchQueryService(db))
        {
            ControllerContext = TestContexts.Platform(app, userId)
        };

    private static T Value<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    [Fact]
    public async Task PublicSearch_Is404_WhenSearchOrPublicSearchIsDisabled()
    {
        var (owner, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("p", "Public page", "visible"));
        await using var db = fixture.CreateContext();

        Assert.Single(Value(await Client(db, app).Search("public")).Items);

        await new SearchAdminService(db).UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(null, null, null, false));
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db, app).Search("public"));
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db, app).Suggest("pub"));
        // The dashboard's "try it" still works.
        Assert.Single(Value(await Platform(db, app, owner.Id).Test("public")).Items);

        await new SearchAdminService(db).UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(false, null, null, true));
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db, app).Search("public"));

        var (_, fresh, _) = await fixture.SeedAppAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => Client(db, fresh).Search("public"));
    }

    [Fact]
    public async Task SettingsDefaults_AreLazyAndOptIn()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var settings = Value(await Platform(db, app, owner.Id).GetSettings());
        Assert.Equal((false, "english", 10_000, true), (settings.Enabled, settings.DefaultLanguage, settings.MaxDocuments,
            settings.PublicSearchEnabled));
        Assert.Contains("french", settings.AvailableLanguages);
        Assert.False(await db.SearchSettings.AnyAsync(s => s.AppId == app.Id));
    }

    [Fact]
    public async Task Overview_Documents_AndDelete_AreTenantScoped()
    {
        var (owner, app, _) = await SeedAppAsync(fixture);
        var (otherOwner, other, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("a", "Alpha 100%", "x", section: "docs"), Page("b", "Beta", "y"));
        await PushAsync(fixture, other.Id, Page("o", "Other app", "z"));
        await using var db = fixture.CreateContext();
        var controller = Platform(db, app, owner.Id);

        var overview = Value(await controller.Overview());
        Assert.Equal((true, 2, 2, 0, app.PublicId), (overview.Enabled, overview.Total, overview.Pages, overview.Items, overview.PublicId));

        var all = Value(await controller.Documents());
        Assert.Equal(2, all.Total);
        Assert.Equal(["Alpha 100%"], Value(await controller.Documents(q: "100%")).Items.Select(d => d.Title));
        Assert.Empty(Value(await controller.Documents(q: "_")).Items);
        Assert.Equal(["Alpha 100%"], Value(await controller.Documents(section: "docs")).Items.Select(d => d.Title));

        var otherDocId = (await DocumentsAsync(fixture, other.Id)).Single().Id;
        await Assert.ThrowsAsync<NotFoundException>(() => controller.DeleteDocument(otherDocId));
        Assert.Single(await DocumentsAsync(fixture, other.Id));

        var mine = all.Items.First();
        Assert.IsType<NoContentResult>(await controller.DeleteDocument(mine.Id));
        Assert.Single(await DocumentsAsync(fixture, app.Id));

        var otherJob = await new SearchAdminService(db).EnqueueReindexAsync(other.Id, null);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetJob(otherJob.Id));
        _ = otherOwner;
    }

    [Fact]
    public async Task ReindexEndpoint_ReturnsAcceptedJob_AndTwiceReturnsTheSameJob()
    {
        var (owner, app, _) = await SeedAppAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = Platform(db, app, owner.Id);
        var first = Assert.IsType<AcceptedAtActionResult>((await controller.Reindex()).Result);
        var second = Assert.IsType<AcceptedAtActionResult>((await controller.Reindex()).Result);
        Assert.Equal(((ReindexJobDto)first.Value!).Id, ((ReindexJobDto)second.Value!).Id);
        Assert.Single(Value(await controller.ListJobs()));
    }

    [Fact]
    public async Task Analytics_TopAndZeroResultQueries_Last30Days_AndCleanupPurgesOldRows()
    {
        var (owner, app, _) = await SeedAppAsync(fixture);
        var (_, other, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("p", "Pricing", "plans"));
        await using var db = fixture.CreateContext();
        var client = Client(db, app);
        await client.Search("pricing");
        await client.Search("Pricing");
        await client.Search("refund policy");
        await Client(db, other).Search("pricing");
        db.SearchQueryLogs.Add(new SearchQueryLog { AppId = app.Id, Query = "ancient", ResultCount = 0 });
        await db.SaveChangesAsync();
        await db.SearchQueryLogs.Where(l => l.Query == "ancient")
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, DateTime.UtcNow.AddDays(-31)));

        var analytics = Value(await Platform(db, app, owner.Id).Analytics());
        Assert.Equal(3, analytics.TotalQueries);
        Assert.Equal([("pricing", 2), ("refund policy", 1)], analytics.TopQueries.Select(q => (q.Query, q.Count)));
        Assert.Equal(["refund policy"], analytics.ZeroResultQueries.Select(q => q.Query));

        var purged = await new SearchQueryLogCleanupWorker(fixture.CreateScopeFactory(),
            NullLogger<SearchQueryLogCleanupWorker>.Instance).PurgeAsync();
        Assert.True(purged >= 1);
        Assert.False(await db.SearchQueryLogs.AnyAsync(l => l.Query == "ancient"));
        Assert.Equal(4, await db.SearchQueryLogs.CountAsync(l => l.AppId == app.Id || l.AppId == other.Id));
    }

    [Fact]
    public async Task ConfigEndpoints_ListGetPutDelete()
    {
        var (owner, app, collection) = await SeedAppAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = Platform(db, app, owner.Id);

        var saved = Value(await controller.PutConfig(collection.Slug,
            new PutCollectionConfigRequest(true, "/title", ["/body"], "/tags", "/p/{id}", "posts", "simple")));
        Assert.Equal(("simple", "posts", collection.Slug), (saved.Config.Language, saved.Config.Section, saved.Config.CollectionSlug));
        Assert.NotNull(saved.ReindexJob);

        Assert.Single(Value(await controller.ListConfigs()));
        Assert.Equal("/p/{id}", Value(await controller.GetConfig(collection.Slug)).UrlTemplate);
        Assert.IsType<NoContentResult>(await controller.DeleteConfig(collection.Slug));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetConfig(collection.Slug));
    }

    [Fact]
    public async Task ConcurrentConfigPuts_LeaveOneConfig()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            ConfigureCollectionAsync(fixture, app.Id, collection.Slug, section: $"s{i}")));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.SearchCollectionConfigs.CountAsync(c => c.AppId == app.Id));
        Assert.Equal(1, await db.SearchReindexJobs.CountAsync(j => j.AppId == app.Id && j.Status == ReindexStatuses.Pending));
    }
}
