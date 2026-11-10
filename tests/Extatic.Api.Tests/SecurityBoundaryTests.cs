using System.Security.Claims;
using System.Net;
using Extatic.Api.Auth;
using Extatic.Api.Auth.Requirements;
using Extatic.Api.Controllers.Client;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace Extatic.Api.Tests;

public class SecurityBoundaryTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task DevTokenEndpoint_IsUnavailableOutsideDevelopment()
    {
        var controller = new ClientAuthController(
            new DevTokenService(null!), new AppUserService(null!),
            new TestEnvironment { EnvironmentName = "Production" });

        var response = await controller.GetDevToken(new DevTokenRequest("someone@example.com", "Someone"));

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task AppUserPolicy_RejectsTokenForDifferentApp()
    {
        var app = new App { Id = Guid.NewGuid() };
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CurrentApp"] = app;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AppClaimTypes.AppId, Guid.NewGuid().ToString()),
            new Claim(AppClaimTypes.AppUserId, Guid.NewGuid().ToString())
        ], "test"));
        var requirement = new AppUserAppRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, httpContext);

        await new AppUserAppHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AppUserPolicy_AllowsTokenForRequestedApp()
    {
        var app = new App { Id = Guid.NewGuid() };
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CurrentApp"] = app;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AppClaimTypes.AppId, app.Id.ToString()),
            new Claim(AppClaimTypes.AppUserId, Guid.NewGuid().ToString())
        ], "test"));
        var requirement = new AppUserAppRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, httpContext);

        await new AppUserAppHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/metadata")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://user:password@example.com/hook")]
    public void WebhookDestination_RejectsUnsafeUrls(string url)
        => Assert.False(WebhookDestination.IsValidUrl(url));

    [Fact]
    public void WebhookDestination_RejectsResolvedPrivateAddresses()
    {
        Assert.False(WebhookDestination.IsPublicAddress(IPAddress.Parse("10.0.0.1")));
        Assert.False(WebhookDestination.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.True(WebhookDestination.IsPublicAddress(IPAddress.Parse("1.1.1.1")));
        Assert.True(WebhookDestination.IsValidUrl("https://example.com/hook"));
    }

    [Fact]
    public async Task AttachmentWrites_DenyAnotherItemsOwner()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var owner = await fixture.SeedAppUserAsync(app.Id);
        var other = await fixture.SeedAppUserAsync(app.Id);
        await using var db = fixture.CreateContext();
        var item = new Item { CollectionId = collection.Id, AppUserId = owner.Id, Data = "{}" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var dispatcher = new WebhookDispatcher(fixture.CreateScopeFactory(), new WebhookPayloadBuilder());
        var controller = new ClientAttachmentsController(
            new CollectionService(db),
            new ItemService(db, new JsonSchemaValidator(), dispatcher),
            new AttachmentService(db, null!));
        var httpContext = new DefaultHttpContext { User = ApiTestFixture.AppUserPrincipal(other.Id) };
        httpContext.Items["CurrentApp"] = app;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await Assert.ThrowsAsync<ForbiddenException>(() => controller.Upload(collection.Slug, item.Id, null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => controller.Delete(collection.Slug, item.Id, Guid.NewGuid()));
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Extatic.Api.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
