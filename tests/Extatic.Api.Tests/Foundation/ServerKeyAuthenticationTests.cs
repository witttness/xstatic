using Extatic.Api.Auth;
using Extatic.Api.Auth.Schemes;
using Extatic.Api.Common;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Foundation;

public class ServerKeyAuthenticationTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private async Task<(App App, string Key)> SeedAppWithKeyAsync()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var key = "exk_" + Tokens.NewUrlToken();
        await using var db = fixture.CreateContext();
        await db.Apps.Where(a => a.Id == app.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ApiKeyHash, Tokens.Hash(key)));
        return (app, key);
    }

    private static Dictionary<string, string> KeyHeader(string key)
        => new() { [ServerKeyAuthenticationHandler.HeaderName] = key };

    [Fact]
    public async Task ValidKey_AuthenticatesAndSetsCurrentApp()
    {
        var (app, key) = await SeedAppWithKeyAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{app.PublicId}/forms", KeyHeader(key));

        var result = await pipeline.AuthorizeAsync(context, PolicyNames.AppServerKey);

        Assert.True(result.Succeeded);
        Assert.Equal(app.Id, Assert.IsType<App>(context.Items["CurrentApp"]).Id);
        Assert.Equal(AuthSchemes.ServerKey, context.User.Identity?.AuthenticationType);
    }

    [Fact]
    public async Task WrongKey_IsRejected()
    {
        var (app, _) = await SeedAppWithKeyAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{app.PublicId}/forms", KeyHeader("exk_wrong"));

        var result = await pipeline.AuthorizeAsync(context, PolicyNames.AppServerKey);

        Assert.False(result.Succeeded);
        Assert.True(result.Challenged);
    }

    [Fact]
    public async Task KeyForAnotherApp_IsRejected()
    {
        var (_, otherKey) = await SeedAppWithKeyAsync();
        var (target, _) = await SeedAppWithKeyAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{target.PublicId}/forms", KeyHeader(otherKey));

        var authenticate = await context.AuthenticateAsync(AuthSchemes.ServerKey);
        Assert.False(authenticate.Succeeded);
        Assert.False((await pipeline.AuthorizeAsync(context, PolicyNames.AppServerKey)).Succeeded);
    }

    [Fact]
    public async Task MissingKey_IsRejected_EvenThoughDefaultApiKeySchemeSucceeds()
    {
        var (app, _) = await SeedAppWithKeyAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{app.PublicId}/forms");

        // SmartSelector routes app_ paths to ApiKey, which authenticates on the public id alone...
        Assert.True((await context.AuthenticateAsync()).Succeeded);
        // ...but the server-key policy must not accept that identity.
        var result = await pipeline.AuthorizeAsync(context, PolicyNames.AppServerKey);
        Assert.False(result.Succeeded);
    }
}
