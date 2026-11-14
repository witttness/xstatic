using Extatic.Api.Auth;
using Extatic.Api.Controllers;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests.Foundation;

public class ClientAuthTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public void AppUserId_IsReturnedForTokenOfCurrentApp()
    {
        var app = new App { Id = Guid.NewGuid() };
        var appUserId = Guid.NewGuid();
        var controller = new ProbeController { ControllerContext = TestContexts.Client(app, appUserId) };

        Assert.Equal(appUserId, controller.ProbeAppUserId);
        Assert.Equal(appUserId, controller.ProbeRequire());
        Assert.Same(app, controller.ProbeApp);
    }

    [Fact]
    public void TokenForAnotherApp_IsTreatedAsAnonymous()
    {
        var app = new App { Id = Guid.NewGuid() };
        var controller = new ProbeController
        {
            ControllerContext = TestContexts.Client(app, Guid.NewGuid(), tokenAppId: Guid.NewGuid())
        };

        Assert.Null(controller.ProbeAppUserId);
        Assert.Throws<UnauthorizedException>(() => controller.ProbeRequire());
    }

    [Fact]
    public void Anonymous_HasNoAppUser()
    {
        var controller = new ProbeController { ControllerContext = TestContexts.Client(new App { Id = Guid.NewGuid() }) };
        Assert.Null(controller.ProbeAppUserId);
    }

    [Fact]
    public async Task ClientAnonymousOrAppUserPolicy_AnonymousAndCrossTenantTokenAreAnonymous()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var user = await fixture.SeedAppUserAsync(app.Id);
        var foreignUser = await fixture.SeedAppUserAsync(otherApp.Id);
        using var pipeline = new TestAuthPipeline(fixture);
        var tokens = TestServices.AppUserTokenService(pipeline.Configuration);

        async Task<Guid?> Resolve(string? token)
        {
            var headers = token is null ? null : new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
            var context = pipeline.CreateContext($"/api/{app.PublicId}/comments", headers);
            var result = await pipeline.AuthorizeAsync(context, PolicyNames.ClientAnonymousOrAppUser);
            Assert.True(result.Succeeded);
            var probe = new ProbeController { ControllerContext = new ControllerContext { HttpContext = context } };
            return probe.ProbeAppUserId;
        }

        Assert.Null(await Resolve(null));
        Assert.Equal(user.Id, await Resolve(tokens.CreateAccessToken(user)));
        Assert.Null(await Resolve(tokens.CreateAccessToken(foreignUser)));
        Assert.Null(await Resolve("not-a-jwt"));
    }

    [Fact]
    public async Task ClientAnonymousOrAppUserPolicy_RejectsUnknownApp()
    {
        await fixture.EnsureDatabaseAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext("/api/app_doesnotexist/comments");
        Assert.False((await pipeline.AuthorizeAsync(context, PolicyNames.ClientAnonymousOrAppUser)).Succeeded);
    }

    private sealed class ProbeController : ClientControllerBase
    {
        public Guid? ProbeAppUserId => AppUserId;
        public App ProbeApp => CurrentApp;
        public Guid ProbeRequire() => RequireAppUserId();
    }
}
