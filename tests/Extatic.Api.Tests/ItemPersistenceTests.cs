using System.Text.Json;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class ItemPersistenceTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private const string Schema = """{"type":"object","required":["title"],"properties":{"title":{"type":"string"}}}""";

    [Fact]
    public async Task Create_StagesDeliveryAndStartsVersionOne()
    {
        var (_, app, collection) = await fixture.SeedAppAsync(Schema);
        await SeedWebhookAsync(app.Id);
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var item = await service.CreateAsync(collection.Id, null, JsonSerializer.SerializeToElement(new { title = "A" }), Schema, app.Id);

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, (await verify.Items.SingleAsync(i => i.Id == item.Id)).Version);
        var log = await verify.WebhookDeliveryLogs.SingleAsync(l => l.Webhook.AppId == app.Id);
        Assert.Equal(0, log.AttemptNumber);
        Assert.Null(log.ClaimedUntil);
        Assert.True(log.NextRetryAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task RejectedWrite_DoesNotChangeItemOrStageDelivery()
    {
        var (_, app, collection) = await fixture.SeedAppAsync(Schema);
        await SeedWebhookAsync(app.Id);
        await using var db = fixture.CreateContext();
        var service = Service(db);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(collection.Id, null,
            JsonSerializer.SerializeToElement(new { wrong = "A" }), Schema, app.Id));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.CollectionId == collection.Id));
        Assert.False(await verify.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task Update_IncrementsVersionAndRejectsStaleDraftWithoutDelivery()
    {
        var (_, app, collection) = await fixture.SeedAppAsync(Schema);
        await SeedWebhookAsync(app.Id);
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var item = await service.CreateAsync(collection.Id, null, JsonSerializer.SerializeToElement(new { title = "A" }), Schema, app.Id);
        await service.UpdateAsync(item, JsonSerializer.SerializeToElement(new { title = "B" }), Schema, app.Id, 1);
        Assert.Equal(2, item.Version);
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(item,
            JsonSerializer.SerializeToElement(new { wrong = "C" }), Schema, app.Id, 2));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(item,
            JsonSerializer.SerializeToElement(new { title = "C" }), Schema, app.Id, 1));
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id));
        Assert.Contains("B", (await verify.Items.SingleAsync(i => i.Id == item.Id)).Data);
    }

    [Fact]
    public async Task WebhookInsertFailure_RollsBackItem()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        await SeedWebhookAsync(app.Id);
        await using (var setup = fixture.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_test_delivery() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'test delivery insert failure'; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER reject_test_delivery_trigger BEFORE INSERT ON webhook_delivery_logs
                FOR EACH ROW EXECUTE FUNCTION reject_test_delivery();
                """);
        }
        try
        {
            await using var db = fixture.CreateContext();
            await Assert.ThrowsAnyAsync<Exception>(() => Service(db).CreateAsync(collection.Id, null,
                JsonSerializer.SerializeToElement(new { title = "A" }), null, app.Id));
            await using var verify = fixture.CreateContext();
            Assert.False(await verify.Items.AnyAsync(i => i.CollectionId == collection.Id));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_delivery_trigger ON webhook_delivery_logs; DROP FUNCTION reject_test_delivery();");
        }
    }

    private async Task SeedWebhookAsync(Guid appId)
    {
        await using var db = fixture.CreateContext();
        db.Webhooks.Add(new Webhook { AppId = appId, Url = "https://example.test/hook", Secret = "secret", Events = ["item.created", "item.updated"] });
        await db.SaveChangesAsync();
    }

    private ItemService Service(Extatic.Api.Data.AppDbContext db) =>
        new(db, new JsonSchemaValidator(), new WebhookDispatcher(fixture.CreateScopeFactory(), new WebhookPayloadBuilder()));
}
