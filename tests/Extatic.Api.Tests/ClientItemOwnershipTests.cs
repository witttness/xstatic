using System.Text.Json;
using Extatic.Api.Controllers.Client;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class ClientItemOwnershipTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Update_DeniesAnotherAppUsersItem()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var owner = await fixture.SeedAppUserAsync(app.Id);
        var other = await fixture.SeedAppUserAsync(app.Id);
        var item = await SeedItemAsync(collection.Id, owner.Id, withAttachment: false);

        await using var db = fixture.CreateContext();
        var controller = CreateController(db, app, other.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => controller.Update(
            collection.Slug, item.Id,
            new Extatic.Api.Dtos.UpdateItemRequest(JsonSerializer.SerializeToElement(new { title = "changed" }))));

        await using var verify = fixture.CreateContext();
        Assert.Equal("{}", (await verify.Items.SingleAsync(i => i.Id == item.Id)).Data);
    }

    [Fact]
    public async Task Delete_DeniesAnotherAppUsersItemAndKeepsAttachment()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var owner = await fixture.SeedAppUserAsync(app.Id);
        var other = await fixture.SeedAppUserAsync(app.Id);
        var item = await SeedItemAsync(collection.Id, owner.Id, withAttachment: true);

        await using var db = fixture.CreateContext();
        var controller = CreateController(db, app, other.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => controller.Delete(collection.Slug, item.Id));

        await using var verify = fixture.CreateContext();
        Assert.True(await verify.Items.AnyAsync(i => i.Id == item.Id));
        Assert.True(await verify.Attachments.AnyAsync(a => a.ItemId == item.Id));
    }

    [Fact]
    public async Task Update_AllowsOwnerAndAdvancesVersion()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var owner = await fixture.SeedAppUserAsync(app.Id);
        var item = await SeedItemAsync(collection.Id, owner.Id, withAttachment: false);

        await using var db = fixture.CreateContext();
        var controller = CreateController(db, app, owner.Id);
        var result = await controller.Update(collection.Slug, item.Id,
            new Extatic.Api.Dtos.UpdateItemRequest(JsonSerializer.SerializeToElement(new { title = "changed" })));

        Assert.IsType<OkObjectResult>(result.Result);
        await using var verify = fixture.CreateContext();
        var saved = await verify.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(2, saved.Version);
        Assert.Contains("changed", saved.Data);
    }

    [Fact]
    public async Task Delete_AllowsOwner()
    {
        var (_, app, collection) = await fixture.SeedAppAsync();
        var owner = await fixture.SeedAppUserAsync(app.Id);
        var item = await SeedItemAsync(collection.Id, owner.Id, withAttachment: false);
        await using var db = fixture.CreateContext();
        var controller = CreateController(db, app, owner.Id);
        Assert.IsType<NoContentResult>(await controller.Delete(collection.Slug, item.Id));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Items.AnyAsync(i => i.Id == item.Id));
    }

    private async Task<Item> SeedItemAsync(Guid collectionId, Guid ownerId, bool withAttachment)
    {
        await using var db = fixture.CreateContext();
        var item = new Item { CollectionId = collectionId, AppUserId = ownerId, Data = "{}" };
        if (withAttachment)
            item.Attachments.Add(new Attachment
            {
                AppUserId = ownerId,
                Filename = "test.txt",
                ContentType = "text/plain",
                StoragePath = "test/path",
                Url = "https://example.test/test.txt"
            });
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private ClientItemsController CreateController(AppDbContext db, App app, Guid appUserId)
    {
        var dispatcher = new WebhookDispatcher(fixture.CreateScopeFactory(), new WebhookPayloadBuilder());
        var itemService = new ItemService(db, new JsonSchemaValidator(), dispatcher);
        var controller = new ClientItemsController(
            new CollectionService(db), itemService, new AttachmentService(db, null!));
        var context = new DefaultHttpContext { User = ApiTestFixture.AppUserPrincipal(appUserId) };
        context.Items["CurrentApp"] = app;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
