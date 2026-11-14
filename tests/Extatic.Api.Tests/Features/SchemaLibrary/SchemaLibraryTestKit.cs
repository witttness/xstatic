using System.Reflection;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Auth.Handlers;
using Extatic.Api.Auth.Requirements;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Domain.Enums;
using Extatic.Api.Features.SchemaLibrary;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests.Features.SchemaLibrary;

public enum Actor { PlatformAdmin, Owner, AppAdmin, Editor, Viewer, Outsider }

/// <summary>An App with one member per role, a platform admin (who is also an editor of the App) and schemas of every kind.</summary>
public sealed record LibraryWorld(
    App App,
    App OtherApp,
    Dictionary<Actor, (Guid UserId, string Email)> Actors,
    IPlatformAdminService PlatformAdmins,
    Guid GlobalAdminOnlyId,
    Guid GlobalSharedId,
    Guid OwnAppId,
    Guid OtherAppId);

public static class SchemaLibraryTestKit
{
    public static JsonElement JsonOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement SimpleSchema(string property = "title") =>
        JsonOf($$$"""{"type":"object","properties":{"{{{property}}}":{"type":"string"}},"required":["{{{property}}}"]}""");

    public static SchemaLibraryService Service(AppDbContext db) => new(db, new JsonSchemaValidator());

    public static string Slug(string prefix = "s") => $"{prefix}-{Guid.NewGuid():N}"[..20];

    public static T Value<T>(ActionResult<T> result) =>
        result.Value ?? (T)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    public static async Task<LibraryWorld> SeedWorldAsync(ApiTestFixture fixture)
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var admin = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Admin);
        var editor = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Editor);
        var viewer = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Viewer);
        var platformAdmin = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Editor);
        var outsider = await fixture.SeedCollaboratorAsync(otherApp.Id, CollaboratorRole.Admin);

        await using var db = fixture.CreateContext();
        var actors = new Dictionary<Actor, (Guid, string)>();
        foreach (var (actor, collaborator) in new[]
                 {
                     (Actor.AppAdmin, admin), (Actor.Editor, editor), (Actor.Viewer, viewer),
                     (Actor.PlatformAdmin, platformAdmin), (Actor.Outsider, outsider)
                 })
        {
            var user = await db.Users.FindAsync(collaborator.UserId);
            actors[actor] = (user!.Id, user.Email);
        }
        actors[Actor.Owner] = (owner.Id, owner.Email);
        var admins = TestServices.PlatformAdmins(actors[Actor.PlatformAdmin].Item2);

        var service = Service(db);
        var operatorId = actors[Actor.PlatformAdmin].Item1;
        var adminOnly = await service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("Draft", Slug("draft"), SimpleSchema(), Visibility: "admin_only"), operatorId);
        var shared = await service.CreateAsync(LibraryScope.Global,
            new CreateLibrarySchemaRequest("Blog Post", Slug("post"), SimpleSchema(), Visibility: "shared",
                Category: "Content", Tags: ["Blog"]), operatorId);
        var own = await service.CreateAsync(LibraryScope.ForApp(app.Id),
            new CreateLibrarySchemaRequest("Own Product", Slug("product"), SimpleSchema("sku")), owner.Id);
        var other = await service.CreateAsync(LibraryScope.ForApp(otherApp.Id),
            new CreateLibrarySchemaRequest("Other Secret", Slug("secret"), SimpleSchema()), outsider.UserId);

        return new LibraryWorld(app, otherApp, actors, admins, adminOnly.Id, shared.Id, own.Id, other.Id);
    }

    public static AppSchemasController AppController(AppDbContext db, LibraryWorld world, Actor actor, App? app = null)
    {
        var (userId, email) = world.Actors[actor];
        return new AppSchemasController(Service(db), world.PlatformAdmins)
        {
            ControllerContext = TestContexts.Platform(app ?? world.App, userId, email)
        };
    }

    /// <summary>
    /// Runs the real <see cref="AppRoleHandler"/> for every App policy declared on the action (class + method
    /// [Authorize] attributes), as the authorization middleware would.
    /// </summary>
    public static async Task<bool> IsAuthorizedAsync(ApiTestFixture fixture, Type controller, string action,
        App app, Guid userId)
    {
        var method = controller.GetMethod(action) ?? throw new InvalidOperationException(action);
        var policies = controller.GetCustomAttributes<AuthorizeAttribute>(true)
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>(true))
            .Select(a => a.Policy!)
            .ToList();
        foreach (var policy in policies)
        {
            var requirement = policy switch
            {
                PolicyNames.AppAnyAccess => new AppRoleRequirement(CollaboratorRole.Viewer),
                PolicyNames.AppOwnerOrEditor => new AppRoleRequirement(CollaboratorRole.Editor),
                PolicyNames.AppOwnerOrAdmin => new AppRoleRequirement(CollaboratorRole.Admin),
                PolicyNames.AppOwnerOnly => new AppRoleRequirement(null, ownerOnly: true),
                _ => throw new InvalidOperationException($"Unexpected policy {policy}")
            };
            var http = new DefaultHttpContext();
            http.Items["CurrentApp"] = app;
            var handler = new AppRoleHandler(fixture.CreateScopeFactory(), new HttpContextAccessor { HttpContext = http });
            var context = new AuthorizationHandlerContext([requirement], ApiTestFixture.PlatformPrincipal(userId), null);
            await handler.HandleAsync(context);
            if (!context.HasSucceeded) return false;
        }
        return true;
    }
}
