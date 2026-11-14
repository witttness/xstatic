using Extatic.Api.Modules;
using Extatic.Api.Services;
using Extatic.Api.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Extatic.Api.Tests.Foundation;

public class WebhookEventCatalogTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Create_RejectsUnknownEvent_With422Errors()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var service = new WebhookService(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(app.Id, "https://example.com/hook", ["item.created", "appuser.created"]));

        var error = Assert.Single(ex.Errors);
        Assert.Equal("events/1", error.Pointer);
    }

    [Fact]
    public async Task CreateAndUpdate_AcceptModuleEvents_RejectUnknownOnUpdate()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var service = new WebhookService(db, new WebhookEventCatalog([new EventsModule()]));

        var webhook = await service.CreateAsync(app.Id, "https://example.com/hook", ["form.submitted", "item.deleted"]);
        Assert.Equal(["form.submitted", "item.deleted"], webhook.Events);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateAsync(webhook, null, ["bogus.event"], null));
        var updated = await service.UpdateAsync(webhook, null, null, false);
        Assert.False(updated.IsActive);
    }

    private sealed class EventsModule : IFeatureModule
    {
        public string Key => "forms-test";
        public IReadOnlyList<string> WebhookEvents => ["form.submitted"];
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }
}
