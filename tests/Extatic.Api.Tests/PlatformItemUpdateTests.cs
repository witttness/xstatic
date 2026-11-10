using System.Text.Json;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class PlatformItemUpdateTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Update_UsesCurrentSchemaAndPreservesIdentity()
    {
        const string schema = """{"type":"object","required":["title"],"properties":{"title":{"type":"string"}}}""";
        var (owner, app, collection) = await fixture.SeedAppAsync(schema);
        var item = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id, "{\"legacy\":true}");
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var detail = await controller.Get(collection.Slug, item.Id);
        Assert.IsType<OkObjectResult>(detail.Result);
        await Assert.ThrowsAsync<ValidationException>(() => controller.Update(collection.Slug, item.Id,
            new PlatformUpdateItemRequest(JsonSerializer.SerializeToElement(new { wrong = 1 }), 1)));
        var response = await controller.Update(collection.Slug, item.Id,
            new PlatformUpdateItemRequest(JsonSerializer.SerializeToElement(new { title = "New" }), 1));
        var updated = Assert.IsType<ItemDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(item.Id, updated.Id);
        Assert.Equal(collection.Id, updated.CollectionId);
        Assert.Equal(2, updated.Version);
        Assert.Null(updated.AppUserId);
        Assert.IsType<BadRequestObjectResult>((await controller.Update(collection.Slug, item.Id,
            new PlatformUpdateItemRequest(JsonSerializer.SerializeToElement(new { title = "Again" }), 0))).Result);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, (await verify.Items.SingleAsync(i => i.Id == item.Id)).Version);
    }

    [Fact]
    public async Task Update_RejectsStaleVersion()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        var item = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        await Assert.ThrowsAsync<ConflictException>(() => controller.Update(collection.Slug, item.Id,
            new PlatformUpdateItemRequest(JsonSerializer.SerializeToElement(new { title = "X" }), 2)));
    }
}
