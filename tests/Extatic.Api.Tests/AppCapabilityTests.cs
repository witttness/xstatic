using Extatic.Api.Domain.Enums;
using Extatic.Api.Auth.Handlers;
using Extatic.Api.Auth.Requirements;
using Extatic.Api.Auth;
using Extatic.Api.Controllers.Platform;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Extatic.Api.Tests;

public class AppCapabilityTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task OwnerAndAcceptedManagersCanManageItems_ViewerAndInviteeCannot()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var editor = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Editor);
        var admin = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Admin);
        var viewer = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Viewer);
        var invitee = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Admin, accepted: false);
        await using var db = fixture.CreateContext();
        var service = new AppService(db);
        Assert.True(await service.CanManageItemsAsync(app, owner.Id));
        Assert.True(await service.CanManageItemsAsync(app, editor.UserId));
        Assert.True(await service.CanManageItemsAsync(app, admin.UserId));
        Assert.False(await service.CanManageItemsAsync(app, viewer.UserId));
        Assert.False(await service.CanManageItemsAsync(app, invitee.UserId));
    }

    [Fact]
    public async Task AppRoleHandler_DeniesViewerWritesButAllowsReads()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var viewer = await fixture.SeedCollaboratorAsync(app.Id, CollaboratorRole.Viewer);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["CurrentApp"] = app;
        var handler = new AppRoleHandler(fixture.CreateScopeFactory(), accessor);
        var principal = ApiTestFixture.PlatformPrincipal(viewer.UserId);
        var read = new AppRoleRequirement(CollaboratorRole.Viewer);
        var write = new AppRoleRequirement(CollaboratorRole.Editor);
        var context = new AuthorizationHandlerContext([read, write], principal, null);
        await handler.HandleAsync(context);
        Assert.DoesNotContain(read, context.PendingRequirements);
        Assert.Contains(write, context.PendingRequirements);
    }

    [Fact]
    public void PlatformItemRoutes_UseReadAndWritePolicies()
    {
        var controller = typeof(PlatformItemsController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == PolicyNames.AppAnyAccess);
        foreach (var method in new[] { nameof(PlatformItemsController.Create), nameof(PlatformItemsController.Update) })
        {
            Assert.Contains(controller.GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
                attribute => attribute.Policy == PolicyNames.AppOwnerOrEditor);
        }
    }
}
