using Extatic.Api.Dtos;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests;

public class PlatformItemListTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task List_BoundsPagesAndReturnsStableTenantScopedResults()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        var (_, _, otherCollection) = await fixture.SeedAppAsync();
        var sameTime = DateTime.UtcNow.AddMinutes(-1);
        var first = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id, "{\"n\":1}", sameTime);
        var second = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id, "{\"n\":2}", sameTime);
        await PlatformItemTestHelper.SeedItemAsync(fixture, otherCollection.Id);
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var response = await controller.List(collection.Slug, 1, 1);
        var result = Assert.IsType<PagedResult<ItemDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(first.Id.CompareTo(second.Id) > 0 ? first.Id : second.Id, result.Items.Single().Id);
        var next = await controller.List(collection.Slug, 2, 1);
        Assert.Single(Assert.IsType<PagedResult<ItemDto>>(Assert.IsType<OkObjectResult>(next.Result).Value).Items);
        Assert.IsType<BadRequestObjectResult>((await controller.List(collection.Slug, 0, 20)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.List(collection.Slug, 1, 101)).Result);
    }

    [Fact]
    public async Task List_EmptyCollectionIsEmpty()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var response = await controller.List(collection.Slug);
        var result = Assert.IsType<PagedResult<ItemDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task List_NavigatesOneHundredItemsWithoutDuplicates()
    {
        var (owner, app, collection) = await fixture.SeedAppAsync();
        await using (var seed = fixture.CreateContext())
        {
            seed.Items.AddRange(Enumerable.Range(0, 100).Select(n => new Item
            {
                CollectionId = collection.Id,
                Data = $"{{\"number\":{n}}}"
            }));
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.CreateContext();
        var controller = PlatformItemTestHelper.Controller(fixture, db, app, owner.Id);
        var seen = new HashSet<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            var response = await controller.List(collection.Slug, page, 20);
            var result = Assert.IsType<PagedResult<ItemDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
            Assert.Equal(100, result.Total);
            Assert.Equal(20, result.Items.Count);
            foreach (var item in result.Items) Assert.True(seen.Add(item.Id));
        }
        Assert.Equal(100, seen.Count);
    }
}
