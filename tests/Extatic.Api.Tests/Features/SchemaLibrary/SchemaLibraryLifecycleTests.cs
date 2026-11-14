using Extatic.Api.Controllers.Platform;
using Extatic.Api.Dtos;
using Extatic.Api.Features.SchemaLibrary;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.SchemaLibrary.SchemaLibraryTestKit;

namespace Extatic.Api.Tests.Features.SchemaLibrary;

public class SchemaLibraryLifecycleTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static readonly Guid UserA = Guid.NewGuid();

    // ---- versioning ----

    [Fact]
    public async Task ContentChangeBumpsVersion_MetadataAndReorderingDoNot()
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var created = await service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("V", Slug(), JsonOf("""{"type":"object","title":"a"}"""), ChangeNote: "initial"), UserA);
        Assert.Equal(1, created.Version);
        Assert.Equal("admin_only", created.Visibility);

        var meta = await service.UpdateAsync(LibraryScope.Global, created.Id,
            new UpdateLibrarySchemaRequest(Name: "Renamed", Description: "d", Tags: ["X", "x", " y "]), UserA);
        Assert.Equal(1, meta.Version);
        Assert.Equal(["x", "y"], meta.Tags);

        var reordered = await service.UpdateAsync(LibraryScope.Global, created.Id,
            new UpdateLibrarySchemaRequest(Schema: JsonOf("""{ "title": "a",  "type": "object" }""")), UserA);
        Assert.Equal(1, reordered.Version);

        var changed = await service.UpdateAsync(LibraryScope.Global, created.Id,
            new UpdateLibrarySchemaRequest(Schema: JsonOf("""{"type":"object","title":"b"}"""), ChangeNote: "retitle"), UserA);
        Assert.Equal(2, changed.Version);
        Assert.Equal([2, 1], changed.Versions.Select(v => v.Version));
        Assert.Equal("retitle", changed.Versions[0].ChangeNote);

        var v1 = await service.GetVersionAsync(LibraryScope.Global, created.Id, 1);
        Assert.Equal("a", v1.Schema.GetProperty("title").GetString());
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetVersionAsync(LibraryScope.Global, created.Id, 3));
    }

    [Fact]
    public async Task ParallelContentUpdates_ProduceContiguousUniqueVersions()
    {
        await fixture.EnsureDatabaseAsync();
        Guid id;
        await using (var db = fixture.CreateContext())
            id = (await Service(db).CreateAsync(LibraryScope.Global,
                new CreateLibrarySchemaRequest("P", Slug(), SimpleSchema()), UserA)).Id;

        const int writers = 8;
        var tasks = Enumerable.Range(0, writers).Select(async i =>
        {
            await using var db = fixture.CreateContext();
            await Service(db).UpdateAsync(LibraryScope.Global, id,
                new UpdateLibrarySchemaRequest(Schema: SimpleSchema($"field{i}")), UserA);
        });
        await Task.WhenAll(tasks);

        await using var verify = fixture.CreateContext();
        var versions = await verify.LibrarySchemaVersions.Where(v => v.LibrarySchemaId == id)
            .Select(v => v.Version).OrderBy(v => v).ToListAsync();
        Assert.Equal(Enumerable.Range(1, writers + 1), versions);
        Assert.Equal(writers + 1, (await verify.LibrarySchemas.FindAsync(id))!.Version);
    }

    // ---- slugs ----

    [Fact]
    public async Task SlugsAreUniquePerScope()
    {
        var (_, app1, _) = await fixture.SeedAppAsync();
        var (_, app2, _) = await fixture.SeedAppAsync();
        var slug = Slug("dup");
        await using var db = fixture.CreateContext();
        var service = Service(db);
        await service.CreateAsync(LibraryScope.Global, new CreateLibrarySchemaRequest("A", slug, SimpleSchema()), UserA);
        await using (var db2 = fixture.CreateContext())
            await Assert.ThrowsAsync<ConflictException>(() => Service(db2).CreateAsync(LibraryScope.Global,
                new CreateLibrarySchemaRequest("B", slug, SimpleSchema()), UserA));

        // Same slug is fine in each App library (independent of the global one), but not twice in one App.
        await Service(fixture.CreateContext()).CreateAsync(LibraryScope.ForApp(app1.Id), new CreateLibrarySchemaRequest("A", slug, SimpleSchema()), UserA);
        await Service(fixture.CreateContext()).CreateAsync(LibraryScope.ForApp(app2.Id), new CreateLibrarySchemaRequest("A", slug, SimpleSchema()), UserA);
        await Assert.ThrowsAsync<ConflictException>(() => Service(fixture.CreateContext()).CreateAsync(
            LibraryScope.ForApp(app1.Id), new CreateLibrarySchemaRequest("B", slug, SimpleSchema()), UserA));

        // Renaming onto a taken slug conflicts too.
        var other = await Service(fixture.CreateContext()).CreateAsync(LibraryScope.ForApp(app1.Id),
            new CreateLibrarySchemaRequest("C", Slug("c"), SimpleSchema()), UserA);
        await Assert.ThrowsAsync<ConflictException>(() => Service(fixture.CreateContext()).UpdateAsync(
            LibraryScope.ForApp(app1.Id), other.Id, new UpdateLibrarySchemaRequest(Slug: slug), UserA));
    }

    [Fact]
    public async Task ParallelCreatesWithSameSlug_ExactlyOneWins()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var slug = Slug("race");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            await using var db = fixture.CreateContext();
            try
            {
                await Service(db).CreateAsync(LibraryScope.ForApp(app.Id),
                    new CreateLibrarySchemaRequest($"R{i}", slug, SimpleSchema()), UserA);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        }));
        Assert.Equal(1, results.Count(r => r));
    }

    // ---- content validation ----

    [Theory]
    [InlineData("""{"$ref":"https://evil.example/schema.json"}""", "schema/$ref")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"other.json#/x"}}}""", "schema/properties/a/$ref")]
    [InlineData("""{"$defs":{"x":{"$dynamicRef":"http://x/y"}}}""", "schema/$defs/x/$dynamicRef")]
    public async Task ExternalRefsAreRejected(string schema, string pointer)
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("R", Slug(), JsonOf(schema)), UserA));
        Assert.Contains(ex.Errors, e => e.Pointer == pointer);
    }

    [Fact]
    public async Task FragmentRefsAndRefLikeDataAreAccepted()
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        var schema = JsonOf("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$defs": { "name": { "type": "string" } },
              "type": "object",
              "properties": { "n": { "$ref": "#/$defs/name" }, "c": { "const": { "$ref": "https://not-a-ref" } } }
            }
            """);
        var created = await Service(db).CreateAsync(LibraryScope.Global, new CreateLibrarySchemaRequest("F", Slug(), schema), UserA);
        Assert.Equal(1, created.Version);
    }

    [Theory]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"minLength":"x"}""")]
    [InlineData("""{"$schema":"http://json-schema.org/draft-07/schema#","type":"object"}""")]
    public async Task InvalidSchemasAreRejected(string schema)
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("I", Slug(), JsonOf(schema)), UserA));
    }

    [Fact]
    public async Task OversizedSchemaIsRejected()
    {
        await fixture.EnsureDatabaseAsync();
        var big = JsonOf($$"""{"description":"{{new string('x', LibrarySchemaRules.MaxSchemaBytes)}}"}""");
        await using var db = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("Big", Slug(), big), UserA));
        Assert.Contains(ex.Errors, e => e.Message.Contains("256 KB"));
    }

    [Fact]
    public async Task InvalidMetadataIsRejected()
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        var service = Service(db);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("", Slug(), SimpleSchema()), UserA));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("N", "Bad Slug", SimpleSchema()), UserA));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("N", Slug(), SimpleSchema(), Visibility: "public"), UserA));
    }

    [Fact]
    public async Task ValidateEndpoint_ReportsSchemaAndSampleErrors()
    {
        await fixture.EnsureDatabaseAsync();
        await using var db = fixture.CreateContext();
        var controller = new AdminSchemasController(Service(db));

        var ok = Value(controller.Validate(new ValidateSchemaRequest(SimpleSchema(), JsonOf("""{"title":"x"}"""))));
        Assert.True(ok.Valid);
        Assert.Empty(ok.SampleErrors!);

        var badSample = Value(controller.Validate(new ValidateSchemaRequest(SimpleSchema(), JsonOf("""{"title":5}"""))));
        Assert.False(badSample.Valid);
        Assert.Empty(badSample.SchemaErrors);
        Assert.Contains(badSample.SampleErrors!, e => e.Pointer == "/title");

        var noSample = Value(controller.Validate(new ValidateSchemaRequest(SimpleSchema())));
        Assert.True(noSample.Valid);
        Assert.Null(noSample.SampleErrors);

        var badSchema = Value(controller.Validate(new ValidateSchemaRequest(JsonOf("""{"$ref":"http://x"}"""), JsonOf("{}"))));
        Assert.False(badSchema.Valid);
        Assert.NotEmpty(badSchema.SchemaErrors);
        Assert.Null(badSchema.SampleErrors);
    }

    // ---- archive / delete / visibility effects ----

    [Fact]
    public async Task Archive_HidesFromPickerAndBlocksNewAttachment_ButExistingProvenanceSurvives()
    {
        var w = await SeedWorldAsync(fixture);
        var owner = w.Actors[Actor.Owner];
        Guid collectionId;
        await using (var db = fixture.CreateContext())
        {
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            collectionId = Value(await collections.Create(new CreateCollectionRequest("Posts", "posts", "{}",
                SourceSchemaId: w.GlobalSharedId))).Id;
        }
        await using (var db = fixture.CreateContext())
            await Service(db).SetArchivedAsync(LibraryScope.Global, w.GlobalSharedId, true, UserA);

        await using (var db = fixture.CreateContext())
        {
            var picker = AppController(db, w, Actor.Owner);
            Assert.DoesNotContain(Value(await picker.Available(null, null)).Items, a => a.Id == w.GlobalSharedId);

            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            await Assert.ThrowsAsync<ValidationException>(() => collections.Create(new CreateCollectionRequest("Other", "other", "{}",
                SourceSchemaId: w.GlobalSharedId)));

            // Re-saving the existing collection with unchanged provenance still works.
            var resaved = Value(await collections.Update("posts", new UpdateCollectionRequest("Posts 2", null, null, null,
                SourceSchemaId: w.GlobalSharedId, SourceSchemaVersion: 1)));
            Assert.Equal(w.GlobalSharedId, resaved.SourceSchemaId);
            Assert.Equal("Blog Post", resaved.SourceSchemaName);
            Assert.Equal(1, resaved.SourceSchemaLatestVersion);
        }

        await using (var db = fixture.CreateContext())
            await Service(db).SetArchivedAsync(LibraryScope.Global, w.GlobalSharedId, false, UserA);
        await using (var db = fixture.CreateContext())
            Assert.Contains(Value(await AppController(db, w, Actor.Owner).Available(null, null)).Items, a => a.Id == w.GlobalSharedId);
        Assert.NotEqual(Guid.Empty, collectionId);
    }

    [Fact]
    public async Task Delete_KeepsCopiedSchemaAndClearsProvenance()
    {
        var w = await SeedWorldAsync(fixture);
        var owner = w.Actors[Actor.Owner];
        await using (var db = fixture.CreateContext())
        {
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            await collections.Create(new CreateCollectionRequest("Posts", "posts", """{"type":"object"}""", SourceSchemaId: w.GlobalSharedId));
            await collections.Create(new CreateCollectionRequest("Products", "products", """{"type":"object"}""", SourceSchemaId: w.OwnAppId));
        }

        await using (var db = fixture.CreateContext())
        {
            var usage = await Service(db).UsageAsync(LibraryScope.Global, w.GlobalSharedId);
            Assert.Equal(("posts", w.App.Slug), (usage.Single().CollectionSlug, usage.Single().AppSlug));
            await new AdminSchemasController(Service(db)).Delete(w.GlobalSharedId);
            await AppController(db, w, Actor.Owner).Delete(w.OwnAppId);
        }

        await using var verify = fixture.CreateContext();
        var rows = await verify.Collections.Where(c => c.AppId == w.App.Id && c.Slug != "items").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, c =>
        {
            Assert.Null(c.SourceSchemaId);
            Assert.Null(c.SourceSchemaVersion);
            Assert.Equal("""{"type": "object"}""", c.Schema);
        });
        Assert.False(await verify.LibrarySchemaVersions.AnyAsync(v => v.LibrarySchemaId == w.GlobalSharedId));
        await using var db2 = fixture.CreateContext();
        Assert.DoesNotContain(Value(await AppController(db2, w, Actor.Owner).Available(null, null)).Items,
            a => a.Id == w.GlobalSharedId || a.Id == w.OwnAppId);
    }

    [Fact]
    public async Task ProvenanceForeignKey_SetsNullOnDelete()
    {
        var w = await SeedWorldAsync(fixture);
        await using (var db = fixture.CreateContext())
        {
            var c = await db.Collections.SingleAsync(x => x.AppId == w.App.Id);
            c.SourceSchemaId = w.OwnAppId;
            c.SourceSchemaVersion = 1;
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"DELETE FROM library_schemas WHERE \"Id\" = {w.OwnAppId}");
        }
        await using var verify = fixture.CreateContext();
        Assert.Null((await verify.Collections.SingleAsync(x => x.AppId == w.App.Id)).SourceSchemaId);
    }

    [Fact]
    public async Task UpdatesAndVisibilityChangesReachCollectionDtos()
    {
        var w = await SeedWorldAsync(fixture);
        var owner = w.Actors[Actor.Owner];
        await using (var db = fixture.CreateContext())
        {
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            await collections.Create(new CreateCollectionRequest("Posts", "posts", "{}", SourceSchemaId: w.GlobalSharedId));
            await Service(db).UpdateAsync(LibraryScope.Global, w.GlobalSharedId,
                new UpdateLibrarySchemaRequest(Schema: SimpleSchema("headline")), UserA);
        }

        async Task<CollectionDto> Read()
        {
            await using var db = fixture.CreateContext();
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            return Value(await collections.Get("posts"));
        }

        var dto = await Read();
        Assert.Equal((1, 2), (dto.SourceSchemaVersion!.Value, dto.SourceSchemaLatestVersion!.Value));

        // Apply the update to v2, then hide the schema from tenants.
        await using (var db = fixture.CreateContext())
        {
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            var applied = Value(await collections.Update("posts", new UpdateCollectionRequest(null, """{"type":"object"}""", null, null,
                SourceSchemaId: w.GlobalSharedId, SourceSchemaVersion: 2)));
            Assert.Equal(2, applied.SourceSchemaVersion);
            await Assert.ThrowsAsync<ValidationException>(() => collections.Update("posts", new UpdateCollectionRequest(null, null, null, null,
                SourceSchemaId: w.GlobalSharedId, SourceSchemaVersion: 9)));
            await Service(db).SetVisibilityAsync(w.GlobalSharedId, "admin_only", UserA);
        }

        dto = await Read();
        Assert.Equal(w.GlobalSharedId, dto.SourceSchemaId);
        Assert.Null(dto.SourceSchemaName);
        Assert.Null(dto.SourceSchemaLatestVersion);

        // Detach.
        await using (var db = fixture.CreateContext())
        {
            var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
            await Assert.ThrowsAsync<ValidationException>(() => collections.Update("posts", new UpdateCollectionRequest(null, null, null, null,
                SourceSchemaId: w.OwnAppId, DetachSourceSchema: true)));
            var detached = Value(await collections.Update("posts", new UpdateCollectionRequest(null, null, null, null, DetachSourceSchema: true)));
            Assert.Null(detached.SourceSchemaId);
            Assert.Null(detached.SourceSchemaVersion);
        }
    }

    [Fact]
    public async Task CollectionsWithoutProvenanceBehaveAsBefore()
    {
        var w = await SeedWorldAsync(fixture);
        var owner = w.Actors[Actor.Owner];
        await using var db = fixture.CreateContext();
        var collections = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
        { ControllerContext = TestContexts.Platform(w.App, owner.UserId, owner.Email) };
        var created = Value(await collections.Create(new CreateCollectionRequest("Plain", "plain", null)));
        Assert.Null(created.SourceSchemaId);
        var updated = Value(await collections.Update("plain", new UpdateCollectionRequest("Plain 2", "{}", true, ["image/png"])));
        Assert.Equal(("Plain 2", true), (updated.Name, updated.AttachmentsEnabled));
        Assert.Null(updated.SourceSchemaVersion);
        await Assert.ThrowsAsync<ValidationException>(() => collections.Create(new CreateCollectionRequest("V", "v", null, SourceSchemaVersion: 1)));
    }

    // ---- DB constraint ----

    [Fact]
    public async Task CheckConstraint_EnforcesScopeAppIdConsistency()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        foreach (var bad in new[]
                 {
                     new LibrarySchema { Scope = LibrarySchemaScope.Global, AppId = app.Id, Visibility = LibrarySchemaVisibility.Shared },
                     new LibrarySchema { Scope = LibrarySchemaScope.App, AppId = null },
                     new LibrarySchema { Scope = LibrarySchemaScope.Global, AppId = null, Visibility = null },
                     new LibrarySchema { Scope = LibrarySchemaScope.App, AppId = app.Id, Visibility = LibrarySchemaVisibility.Shared },
                 })
        {
            await using var db = fixture.CreateContext();
            bad.Name = "x";
            bad.Slug = Slug();
            db.LibrarySchemas.Add(bad);
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ck_library_schemas_scope", ex.InnerException!.Message);
        }
    }
}
