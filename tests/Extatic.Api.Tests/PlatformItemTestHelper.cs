using Extatic.Api.Controllers.Platform;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Tests;

public static class PlatformItemTestHelper
{
    public static PlatformItemsController Controller(ApiTestFixture fixture, AppDbContext db, App app, Guid userId)
    {
        var dispatcher = new WebhookDispatcher(fixture.CreateScopeFactory(), new WebhookPayloadBuilder());
        var controller = new PlatformItemsController(new CollectionService(db),
            new ItemService(db, new JsonSchemaValidator(), dispatcher));
        var context = new DefaultHttpContext { User = ApiTestFixture.PlatformPrincipal(userId) };
        context.Items["CurrentApp"] = app;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    public static async Task<Item> SeedItemAsync(ApiTestFixture fixture, Guid collectionId,
        string data = "{}", DateTime? createdAt = null)
    {
        await using var db = fixture.CreateContext();
        var item = new Item { CollectionId = collectionId, Data = data };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        if (createdAt is not null)
        {
            item.CreatedAt = createdAt.Value;
            await db.SaveChangesAsync();
        }
        return item;
    }
}
