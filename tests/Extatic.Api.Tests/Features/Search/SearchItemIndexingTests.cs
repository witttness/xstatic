using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Search;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Search.SearchTestHelper;

namespace Extatic.Api.Tests.Features.Search;

public class SearchItemIndexingTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private async Task<Item> CreateItemAsync(Collection collection, Guid appId, object data)
    {
        await using var db = fixture.CreateContext();
        return await ItemService(fixture, db).CreateAsync(collection.Id, null, JsonOf(data), null, appId);
    }

    [Fact]
    public async Task Listener_IndexesOnCreateAndUpdate_AndCascadeRemovesOnDelete()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug, bodyPointers: ["/body", "/paragraphs"],
            tagsPointer: "/tags", urlTemplate: "/blog/{slug}", section: "blog");

        var item = await CreateItemAsync(collection, app.Id, new
        {
            title = "First post", slug = "first-post", body = "<p>Hello <b>world</b></p>",
            paragraphs = new[] { "alpha", "beta" }, tags = new[] { "News", "intro" }
        });

        var doc = (await DocumentsAsync(fixture, app.Id)).Single();
        Assert.Equal(("item", item.Id.ToString(), item.Id, collection.Id), (doc.Source, doc.ExternalId, doc.ItemId, doc.CollectionId));
        Assert.Equal("First post", doc.Title);
        Assert.Equal("Hello world alpha beta", doc.Body);
        Assert.Equal(["news", "intro"], doc.Tags);
        Assert.Equal("/blog/first-post", doc.Url);
        Assert.Equal("blog", doc.Section);

        await using (var db = fixture.CreateContext())
        {
            var tracked = await db.Items.SingleAsync(i => i.Id == item.Id);
            await ItemService(fixture, db).UpdateAsync(tracked, JsonOf(new { title = "Renamed post", body = "new words" }), null, app.Id);
        }
        doc = (await DocumentsAsync(fixture, app.Id)).Single();
        Assert.Equal("Renamed post", doc.Title);
        Assert.Null(doc.Url); // {slug} no longer present
        Assert.Single(await TitlesAsync(fixture, app.Id, "renamed"));
        Assert.Empty(await TitlesAsync(fixture, app.Id, "alpha"));

        await using (var db = fixture.CreateContext())
        {
            var tracked = await db.Items.SingleAsync(i => i.Id == item.Id);
            await ItemService(fixture, db).DeleteAsync(tracked, new AttachmentService(db, null!), app.Id);
        }
        Assert.Empty(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task Listener_RespectsConfigAndSettingsEnabled()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await CreateItemAsync(collection, app.Id, new { title = "No config yet" });
        Assert.Empty(await DocumentsAsync(fixture, app.Id));

        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug, enabled: false);
        await CreateItemAsync(collection, app.Id, new { title = "Config disabled" });
        Assert.Empty(await DocumentsAsync(fixture, app.Id));

        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using (var db = fixture.CreateContext())
            await new SearchAdminService(db).UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(false, null, null, null));
        await CreateItemAsync(collection, app.Id, new { title = "Search disabled" });
        Assert.Empty(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task Listener_IsAtomicWithTheItemWrite()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using var db = fixture.CreateContext();
        var service = ItemService(fixture, db, new ThrowingListener());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(collection.Id, null, JsonOf(new { title = "Doomed" }), null, app.Id));

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.CollectionId == collection.Id));
        Assert.Empty(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task MissingFields_ProduceEmptyTitleAndNoUrl()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug, titlePointer: "/name",
            bodyPointers: ["/missing", "/text"], tagsPointer: "/tags", urlTemplate: "/x/{slug}");
        await CreateItemAsync(collection, app.Id, new { text = "only text", tags = "single-tag" });

        var doc = (await DocumentsAsync(fixture, app.Id)).Single();
        Assert.Equal(("", "only text", null as string), (doc.Title, doc.Body, doc.Url));
        Assert.Equal(["single-tag"], doc.Tags);
    }

    [Fact]
    public async Task ConfigValidation_RejectsBadPointersTemplatesAndLanguages()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => ConfigureCollectionAsync(fixture, app.Id,
            collection.Slug, titlePointer: "title", bodyPointers: ["/ok", "bad"], tagsPointer: "nope",
            urlTemplate: "javascript:alert({id})", language: "klingon"));
        Assert.Equal(["/title_pointer", "/body_pointers/1", "/tags_pointer", "/url_template", "/language"],
            ex.Errors.Select(e => e.Pointer));
    }

    [Fact]
    public async Task Config_ForAnotherTenantsCollection_Is404()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        var (_, other, otherCollection) = await SeedAppAsync(fixture);
        await using var db = fixture.CreateContext();
        await db.Collections.Where(c => c.Id == otherCollection.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Slug, "secret-posts"));

        await Assert.ThrowsAsync<NotFoundException>(() => ConfigureCollectionAsync(fixture, app.Id, "secret-posts"));
        await Assert.ThrowsAsync<NotFoundException>(() => new SearchAdminService(db).GetConfigAsync(app.Id, "secret-posts"));
        await Assert.ThrowsAsync<NotFoundException>(() => new SearchAdminService(db).EnqueueReindexAsync(app.Id, otherCollection.Id));
        Assert.Empty(await db.SearchCollectionConfigs.Where(c => c.AppId == other.Id).ToListAsync());
    }

    [Fact]
    public async Task Reindex_IndexesExistingItems_AndIsIdempotent()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        for (var i = 0; i < SearchReindexer.BatchSize + 5; i++)
            await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        await using (var db = fixture.CreateContext())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE items SET \"Data\" = jsonb_build_object('title', 'Seeded ' || \"Id\"::text, 'body', 'reindexed content') WHERE \"CollectionId\" = {collection.Id}");

        var saved = await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        Assert.NotNull(saved.ReindexJob);
        Assert.Equal(ReindexStatuses.Pending, saved.ReindexJob!.Status);

        await Worker(fixture).ProcessDueAsync();
        var docs = await DocumentsAsync(fixture, app.Id);
        Assert.Equal(SearchReindexer.BatchSize + 5, docs.Count);
        await using (var db = fixture.CreateContext())
        {
            var job = await new SearchAdminService(db).GetJobAsync(app.Id, saved.ReindexJob.Id);
            Assert.Equal((ReindexStatuses.Completed, SearchReindexer.BatchSize + 5, SearchReindexer.BatchSize + 5),
                (job.Status, job.Processed, job.Total));
        }

        // Running it again changes nothing (content hashes match).
        var indexedAt = docs.ToDictionary(d => d.Id, d => d.IndexedAt);
        SearchReindexJob secondJob;
        await using (var db = fixture.CreateContext())
            secondJob = await new SearchAdminService(db).EnqueueReindexAsync(app.Id, null);
        await Worker(fixture).ProcessDueAsync();
        var again = await DocumentsAsync(fixture, app.Id);
        Assert.Equal(indexedAt, again.ToDictionary(d => d.Id, d => d.IndexedAt));
        await using (var db = fixture.CreateContext())
            Assert.Equal(ReindexStatuses.Completed, (await new SearchAdminService(db).GetJobAsync(app.Id, secondJob.Id)).Status);
    }

    [Fact]
    public async Task Reindex_TriggeredConcurrently_QueuesOnePendingJob()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug, enabled: false);

        var jobs = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            return await new SearchAdminService(db).EnqueueReindexAsync(app.Id, null);
        }));

        Assert.Single(jobs.Select(j => j.Id).Distinct());
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.SearchReindexJobs.CountAsync(j => j.AppId == app.Id && j.CollectionId == null));
    }

    [Fact]
    public async Task Worker_ConcurrentReplicas_RunAJobOnce()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        var saved = await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Worker(fixture).ProcessDueAsync()));

        await using var db = fixture.CreateContext();
        var job = await db.SearchReindexJobs.SingleAsync(j => j.Id == saved.ReindexJob!.Id);
        Assert.Equal((ReindexStatuses.Completed, 1), (job.Status, job.Attempts));
        Assert.Single(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task Worker_ReclaimsAJobWhoseLeaseExpired()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        var saved = await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using (var db = fixture.CreateContext())
            await db.SearchReindexJobs.Where(j => j.Id == saved.ReindexJob!.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ReindexStatuses.Running)
                .SetProperty(j => j.Attempts, 1)
                .SetProperty(j => j.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        await Worker(fixture).ProcessDueAsync();
        await using var verify = fixture.CreateContext();
        Assert.Equal(ReindexStatuses.Completed, (await verify.SearchReindexJobs.SingleAsync(j => j.Id == saved.ReindexJob!.Id)).Status);
        Assert.Single(await DocumentsAsync(fixture, app.Id));
    }

    [Fact]
    public async Task DisablingOrDeletingConfig_RemovesItsDocuments_AndReenablingSettingsCatchesUp()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await CreateItemAsync(collection, app.Id, new { title = "Kept?" });
        Assert.Single(await DocumentsAsync(fixture, app.Id));

        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug, enabled: false);
        Assert.Empty(await DocumentsAsync(fixture, app.Id));

        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await Worker(fixture).ProcessDueAsync();
        Assert.Single(await DocumentsAsync(fixture, app.Id));

        await using (var db = fixture.CreateContext())
        {
            var admin = new SearchAdminService(db);
            await admin.DeleteConfigAsync(app.Id, collection.Slug);
            await Assert.ThrowsAsync<NotFoundException>(() => admin.DeleteConfigAsync(app.Id, collection.Slug));
        }
        Assert.Empty(await DocumentsAsync(fixture, app.Id));

        // Items written while search is off are picked up when it is switched back on.
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using (var db = fixture.CreateContext())
            await new SearchAdminService(db).UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(false, null, null, null));
        await CreateItemAsync(collection, app.Id, new { title = "Written while off" });
        await Worker(fixture).ProcessDueAsync();
        await using (var db = fixture.CreateContext())
            await new SearchAdminService(db).UpdateSettingsAsync(app.Id, new UpdateSearchSettingsRequest(true, null, null, null));
        await Worker(fixture).ProcessDueAsync();
        Assert.Equal(["Kept?", "Written while off"], (await DocumentsAsync(fixture, app.Id)).Select(d => d.Title));
    }

    [Fact]
    public async Task ReindexAll_DropsDocumentsOfUnconfiguredCollections()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await CreateItemAsync(collection, app.Id, new { title = "Orphan soon" });
        await using (var db = fixture.CreateContext())
        {
            // Simulate drift: the config disappears without its cleanup having run.
            await db.SearchCollectionConfigs.Where(c => c.AppId == app.Id).ExecuteDeleteAsync();
            await new SearchAdminService(db).EnqueueReindexAsync(app.Id, null);
        }
        await Worker(fixture).ProcessDueAsync();
        Assert.Empty(await DocumentsAsync(fixture, app.Id));
    }

    private sealed class ThrowingListener : IItemChangeListener
    {
        public Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created)
            => throw new InvalidOperationException("boom");
        public Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item) => Task.CompletedTask;
    }
}
