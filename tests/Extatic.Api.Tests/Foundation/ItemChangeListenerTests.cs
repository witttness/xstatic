using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Foundation;

public class ItemChangeListenerTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static readonly JsonElement Data = JsonSerializer.SerializeToElement(new { title = "x" });

    [Fact]
    public async Task ListenerWrites_AreCommittedWithTheItem()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var listener = new OutboxWritingListener();
        var service = new ItemService(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture), [listener]);

        var item = await service.CreateAsync(collection.Id, null, Data, null, app.Id);
        await service.UpdateAsync(item, Data, null, app.Id);

        Assert.Equal([(item.Id, true), (item.Id, false)], listener.Saved);
        await using var verify = fixture.CreateContext();
        var categories = await verify.EmailOutbox.Where(m => m.AppId == app.Id)
            .OrderBy(m => m.Category).Select(m => m.Category).ToListAsync();
        Assert.Equal(["item.created", "item.updated"], categories);
    }

    [Fact]
    public async Task ThrowingListener_RollsBackTheItem()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var service = new ItemService(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture),
            [new OutboxWritingListener(), new ThrowingListener()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(collection.Id, null, Data, null, app.Id));

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.CollectionId == collection.Id));
        Assert.False(await verify.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
    }

    [Fact]
    public async Task DeletingListener_RunsBeforeRemoval_AndIsSavedWithIt()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var seeded = await PlatformItemTestHelper.SeedItemAsync(fixture, collection.Id);
        await using var db = fixture.CreateContext();
        var item = await db.Items.SingleAsync(i => i.Id == seeded.Id);
        var listener = new OutboxWritingListener();
        var service = new ItemService(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture), [listener]);

        await service.DeleteAsync(item, new AttachmentService(db, null!), app.Id);

        Assert.True(listener.SawItemInDatabaseOnDelete);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.Id == seeded.Id));
        Assert.True(await verify.EmailOutbox.AnyAsync(m => m.AppId == app.Id && m.Category == "item.deleting"));
    }

    private sealed class OutboxWritingListener : IItemChangeListener
    {
        private readonly IEmailQueue queue = new EmailQueue();
        public List<(Guid, bool)> Saved { get; } = [];
        public bool SawItemInDatabaseOnDelete { get; private set; }

        public Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created)
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Saved.Add((item.Id, created));
            queue.Stage(db, appId, new EmailMessage("listener@example.com", "s", "b", null, null,
                created ? "item.created" : "item.updated"));
            return Task.CompletedTask;
        }

        public async Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item)
        {
            SawItemInDatabaseOnDelete = await db.Items.AnyAsync(i => i.Id == item.Id);
            queue.Stage(db, appId, new EmailMessage("listener@example.com", "s", "b", null, null, "item.deleting"));
        }
    }

    private sealed class ThrowingListener : IItemChangeListener
    {
        public Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created)
            => throw new InvalidOperationException("boom");
        public Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item) => Task.CompletedTask;
    }
}
