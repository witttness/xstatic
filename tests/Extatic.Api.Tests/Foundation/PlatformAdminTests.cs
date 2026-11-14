using Extatic.Api.Auth;
using Extatic.Api.Controllers.Platform;
using Extatic.Api.Dtos;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests.Foundation;

public class PlatformAdminTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Theory]
    [InlineData("admin@example.test", true)]
    [InlineData("ADMIN@Example.Test", true)]
    [InlineData("second@example.test", true)]
    [InlineData("someone@example.test", false)]
    [InlineData("", false)]
    public void IsAdmin_ParsesCommaSeparatedAndIsCaseInsensitive(string email, bool expected)
        => Assert.Equal(expected, TestServices.PlatformAdmins("admin@example.test", " second@example.test ").IsAdmin(email));

    [Fact]
    public void IsAdmin_AcceptsArrayConfiguration()
    {
        var service = new PlatformAdminService(TestServices.Configuration(new Dictionary<string, string?>
        {
            ["Platform:AdminEmails:0"] = "a@example.test",
            ["Platform:AdminEmails:1"] = "b@example.test"
        }));
        Assert.True(service.IsAdmin("b@example.test"));
        Assert.False(service.IsAdmin("c@example.test"));
    }

    [Theory]
    [InlineData("admin@example.test", true)]
    [InlineData("user@example.test", false)]
    public async Task PlatformAdminPolicy_RequiresListedEmail(string email, bool expected)
    {
        await fixture.EnsureDatabaseAsync();
        using var pipeline = new TestAuthPipeline(fixture, TestServices.Configuration(new Dictionary<string, string?>
        {
            ["Platform:AdminEmails"] = "admin@example.test"
        }));
        var context = pipeline.CreateContext("/api/admin/schemas", new Dictionary<string, string>
        {
            ["X-Forwarded-User"] = $"ext-{Guid.NewGuid():N}",
            ["X-Forwarded-Email"] = email
        });

        var result = await pipeline.AuthorizeAsync(context, PolicyNames.PlatformAdmin);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task AuthMe_ReportsIsAdmin()
    {
        var (owner, _, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = new AuthController(db, TestServices.PlatformAdmins(owner.Email))
        {
            ControllerContext = TestContexts.Platform(null, owner.Id, owner.Email)
        };

        var result = await controller.GetMe();

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.IsAdmin);
    }
}
