using Extatic.Api.Auth;
using Extatic.Api.Controllers.Platform;
using Extatic.Api.Dtos;
using Extatic.Api.Features.SchemaLibrary;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using static Extatic.Api.Tests.Features.SchemaLibrary.SchemaLibraryTestKit;

namespace Extatic.Api.Tests.Features.SchemaLibrary;

/// <summary>Visibility matrix: actor × (global AdminOnly / global Shared / own-app / other-app schema).</summary>
public class SchemaLibraryAccessTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static bool CanRead(Actor a) => a is Actor.Owner or Actor.AppAdmin or Actor.Editor or Actor.PlatformAdmin;
    private static bool CanWrite(Actor a) => a is Actor.Owner or Actor.AppAdmin;

    [Theory]
    [InlineData(Actor.PlatformAdmin)]
    [InlineData(Actor.Owner)]
    [InlineData(Actor.AppAdmin)]
    [InlineData(Actor.Editor)]
    [InlineData(Actor.Viewer)]
    [InlineData(Actor.Outsider)]
    public async Task AppApi_PoliciesFollowRoles(Actor actor)
    {
        var w = await SeedWorldAsync(fixture);
        var userId = w.Actors[actor].UserId;
        var t = typeof(AppSchemasController);

        foreach (var read in new[]
                 {
                     nameof(AppSchemasController.List), nameof(AppSchemasController.Get),
                     nameof(AppSchemasController.GetVersion), nameof(AppSchemasController.Usage),
                     nameof(AppSchemasController.Available), nameof(AppSchemasController.GetAvailable),
                     nameof(AppSchemasController.GetAvailableVersion), nameof(AppSchemasController.Validate)
                 })
            Assert.True(CanRead(actor) == await IsAuthorizedAsync(fixture, t, read, w.App, userId), read);

        foreach (var write in new[]
                 {
                     nameof(AppSchemasController.Create), nameof(AppSchemasController.Update),
                     nameof(AppSchemasController.Archive), nameof(AppSchemasController.Unarchive),
                     nameof(AppSchemasController.Delete)
                 })
            Assert.True(CanWrite(actor) == await IsAuthorizedAsync(fixture, t, write, w.App, userId), write);

        // Collection create/update (where provenance is attached) keep their existing owner/admin policy.
        foreach (var action in new[] { nameof(CollectionsController.Create), nameof(CollectionsController.Update) })
            Assert.True(CanWrite(actor) == await IsAuthorizedAsync(fixture, typeof(CollectionsController), action, w.App, userId), action);
    }

    [Theory]
    [InlineData(Actor.PlatformAdmin)]
    [InlineData(Actor.Owner)]
    [InlineData(Actor.AppAdmin)]
    [InlineData(Actor.Editor)]
    public async Task AppApi_ListGetAndPicker_ShowOnlyVisibleSchemas(Actor actor)
    {
        var w = await SeedWorldAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = AppController(db, w, actor);
        var isAdmin = actor == Actor.PlatformAdmin;

        // App library: only the App's own schemas.
        var list = Value(await controller.List(null, null, null));
        Assert.Equal([w.OwnAppId], list.Items.Select(i => i.Id));
        Assert.Equal(w.OwnAppId, Value(await controller.Get(w.OwnAppId)).Id);
        foreach (var id in new[] { w.GlobalSharedId, w.GlobalAdminOnlyId, w.OtherAppId })
            await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(id));

        // Picker.
        var available = Value(await controller.Available(null, null)).Items;
        Assert.Contains(available, a => a.Id == w.GlobalSharedId && a.Scope == "global" && !a.AdminOnly);
        Assert.Contains(available, a => a.Id == w.OwnAppId && a.Scope == "app");
        Assert.DoesNotContain(available, a => a.Id == w.OtherAppId);
        Assert.Equal(isAdmin, available.Any(a => a.Id == w.GlobalAdminOnlyId && a.AdminOnly));

        Assert.Equal(w.GlobalSharedId, Value(await controller.GetAvailable(w.GlobalSharedId)).Id);
        Assert.Equal("sku", Value(await controller.GetAvailable(w.OwnAppId)).Schema.GetProperty("required")[0].GetString());
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAvailable(w.OtherAppId));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAvailableVersion(w.OtherAppId, 1));
        if (isAdmin)
            Assert.True(Value(await controller.GetAvailable(w.GlobalAdminOnlyId)).AdminOnly);
        else
        {
            await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAvailable(w.GlobalAdminOnlyId));
            await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAvailableVersion(w.GlobalAdminOnlyId, 1));
        }
    }

    [Theory]
    [InlineData(Actor.PlatformAdmin, true)]
    [InlineData(Actor.Owner, false)]
    [InlineData(Actor.AppAdmin, false)]
    [InlineData(Actor.Editor, false)]
    [InlineData(Actor.Viewer, false)]
    [InlineData(Actor.Outsider, false)]
    public async Task AdminApi_RequiresPlatformAdmin(Actor actor, bool expected)
    {
        var w = await SeedWorldAsync(fixture);
        using var pipeline = new TestAuthPipeline(fixture, TestServices.Configuration(new Dictionary<string, string?>
        {
            ["Platform:AdminEmails"] = w.Actors[Actor.PlatformAdmin].Email
        }));
        await using var db = fixture.CreateContext();
        var user = await db.Users.FindAsync(w.Actors[actor].UserId);
        var context = pipeline.CreateContext("/api/admin/schemas", new Dictionary<string, string>
        {
            ["X-Forwarded-User"] = user!.ExternalId,
            ["X-Forwarded-Email"] = w.Actors[actor].Email
        });

        var result = await pipeline.AuthorizeAsync(context, PolicyNames.PlatformAdmin);

        Assert.Equal(expected, result.Succeeded);
        var attributes = typeof(AdminSchemasController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, a => a.Policy == PolicyNames.PlatformAdmin);
        Assert.DoesNotContain(typeof(AdminSchemasController).GetMethods(),
            m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);
    }

    [Fact]
    public async Task AdminApi_SeesGlobalSchemasOnly()
    {
        var w = await SeedWorldAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = new AdminSchemasController(Service(db))
        {
            ControllerContext = TestContexts.Platform(null, w.Actors[Actor.PlatformAdmin].UserId, w.Actors[Actor.PlatformAdmin].Email)
        };

        var all = Value(await controller.List(null, null, null, null, 1, 100)).Items.Select(i => i.Id).ToList();
        Assert.Contains(w.GlobalAdminOnlyId, all);
        Assert.Contains(w.GlobalSharedId, all);
        Assert.DoesNotContain(w.OwnAppId, all);
        var shared = Value(await controller.List(null, null, "shared", null, 1, 100)).Items;
        Assert.All(shared, s => Assert.Equal("shared", s.Visibility));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(w.OwnAppId));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Update(w.OwnAppId, new UpdateLibrarySchemaRequest(Name: "x")));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Delete(w.OtherAppId));
    }

    [Fact]
    public async Task AppApi_CannotManageAnotherAppsSchemas()
    {
        var w = await SeedWorldAsync(fixture);
        await using var db = fixture.CreateContext();
        var controller = AppController(db, w, Actor.Owner);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.Update(w.OtherAppId, new UpdateLibrarySchemaRequest(Name: "pwned")));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Archive(w.OtherAppId));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Delete(w.OtherAppId));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetVersion(w.OtherAppId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Usage(w.OtherAppId));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Update(w.GlobalSharedId, new UpdateLibrarySchemaRequest(Name: "pwned")));

        await using var verify = fixture.CreateContext();
        Assert.Equal("Other Secret", (await verify.LibrarySchemas.FindAsync(w.OtherAppId))!.Name);
    }

    [Theory]
    [InlineData(Actor.PlatformAdmin)]
    [InlineData(Actor.Owner)]
    [InlineData(Actor.AppAdmin)]
    [InlineData(Actor.Editor)]
    [InlineData(Actor.Viewer)]
    [InlineData(Actor.Outsider)]
    public async Task AttachingProvenance_FollowsVisibility(Actor actor)
    {
        // Policies (who may call create/update) are covered above; this checks which schemas each caller may attach.
        var w = await SeedWorldAsync(fixture);
        var isAdmin = actor == Actor.PlatformAdmin;
        var expectations = new[]
        {
            (w.GlobalSharedId, true), (w.OwnAppId, true), (w.GlobalAdminOnlyId, isAdmin), (w.OtherAppId, false)
        };
        foreach (var (schemaId, allowed) in expectations)
        {
            await using var db = fixture.CreateContext();
            var (userId, email) = w.Actors[actor];
            var controller = new CollectionsController(new CollectionService(db), w.PlatformAdmins)
            {
                ControllerContext = TestContexts.Platform(w.App, userId, email)
            };
            var request = new CreateCollectionRequest("C", Slug("c"), """{"type":"object"}""",
                SourceSchemaId: schemaId, SourceSchemaVersion: 1);
            if (allowed)
            {
                var dto = Value(await controller.Create(request));
                Assert.Equal(schemaId, dto.SourceSchemaId);
                Assert.Equal(1, dto.SourceSchemaVersion);
                Assert.Equal(1, dto.SourceSchemaLatestVersion);
                Assert.NotNull(dto.SourceSchemaName);
            }
            else
            {
                var ex = await Assert.ThrowsAsync<ValidationException>(() => controller.Create(request));
                Assert.Contains(ex.Errors, e => e.Pointer == "source_schema_id");
            }
        }
    }
}
