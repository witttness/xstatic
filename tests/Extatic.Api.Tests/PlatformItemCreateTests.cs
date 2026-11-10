using System.Text.Json;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class PlatformItemCreateTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Create_ReturnsPlatformLocationAndUnattributedItem()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var response = await controller.Create(collection.Slug, new CreateItemRequest(JsonSerializer.SerializeToElement(new { title = "A" })));
        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var item = Assert.IsType<ItemDto>(created.Value);
        Assert.Null(item.AppUserId);
        Assert.Equal(1, item.Version);
        Assert.Equal(nameof(controller.Get), created.ActionName);
        Assert.Equal(app.Slug, created.RouteValues!["app_slug"]);
        var detail = await controller.Get(collection.Slug, item.Id);
        Assert.IsType<OkObjectResult>(detail.Result);
    }

    [Fact]
    public async Task Create_RejectsNonObjectAndSchemaInvalidDataWithoutItem()
    {
        const string schema = """{"type":"object","required":["title"]}""";
        var (owner, app, collection) = await fixture.SeedAppAsync(schema);
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        await Assert.ThrowsAsync<ValidationException>(() => controller.Create(collection.Slug,
            new CreateItemRequest(JsonSerializer.SerializeToElement(5))));
        await Assert.ThrowsAsync<ValidationException>(() => controller.Create(collection.Slug,
            new CreateItemRequest(JsonSerializer.SerializeToElement(new { other = 1 }))));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.CollectionId == collection.Id));
    }

    [Fact]
    public async Task Create_AcceptsSchemaValidObject()
    {
        const string schema = """{"type":"object","required":["title"],"properties":{"title":{"type":"string"}}}""";
        var (owner, app, collection) = await fixture.SeedAppAsync(schema);
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var response = await controller.Create(collection.Slug,
            new CreateItemRequest(JsonSerializer.SerializeToElement(new { title = "Valid" })));
        Assert.IsType<CreatedAtActionResult>(response.Result);
    }

    [Fact]
    public async Task Detail_IsScopedToAppAndCollection()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        var (_, otherApp, otherCollection) = await fixture.SeedAppAsync();
        var item = await PlatformItemTestHelper.SeedItemAsync(fixture, otherCollection.Id);
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(collection.Slug, item.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(otherCollection.Slug, item.Id));
    }
}
