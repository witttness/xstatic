using System.Text.Json;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class ItemConcurrencyTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task SimultaneousWrites_OnlyOneCommits()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var seeded = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = await firstDb.Items.SingleAsync(i => i.Id == seeded.Id);
        var second = await secondDb.Items.SingleAsync(i => i.Id == seeded.Id);
        var dispatcher = new WebhookDispatcher(fixture.CreateScopeFactory(), new WebhookPayloadBuilder());
        var firstService = new ItemService(firstDb, new JsonSchemaValidator(), dispatcher);
        var secondService = new ItemService(secondDb, new JsonSchemaValidator(), dispatcher);
        await firstService.UpdateAsync(first, JsonSerializer.SerializeToElement(new { n = 1 }), null, app.Id, 1);
        await Assert.ThrowsAsync<ConflictException>(() => secondService.UpdateAsync(second,
            JsonSerializer.SerializeToElement(new { n = 2 }), null, app.Id, 1));
        await using var verify = fixture.CreateContext();
        var saved = await verify.Items.SingleAsync(i => i.Id == seeded.Id);
        Assert.Equal(2, saved.Version);
        Assert.Contains("1", saved.Data);
    }
}
