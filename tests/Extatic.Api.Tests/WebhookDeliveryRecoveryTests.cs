using Extatic.Api.Domain.Entities;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Extatic.Api.Tests;

public class WebhookDeliveryRecoveryTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task DueAndExpiredClaimsAreRecoveredOncePerLease()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using (var db = fixture.CreateContext())
        {
            var webhook = new Webhook { AppId = app.Id, Url = "https://example.test/hook", Secret = "secret", Events = ["item.created"] };
            db.Webhooks.Add(webhook);
            db.WebhookDeliveryLogs.Add(new WebhookDeliveryLog { Webhook = webhook, EventType = "item.created", Payload = "{\"test\":1}", AttemptNumber = 0, NextRetryAt = DateTime.UtcNow.AddSeconds(-1) });
            db.WebhookDeliveryLogs.Add(new WebhookDeliveryLog { Webhook = webhook, EventType = "item.created", Payload = "{\"test\":2}", AttemptNumber = 1, ClaimedUntil = DateTime.UtcNow.AddSeconds(-1) });
            await db.SaveChangesAsync();
        }
        var factory = fixture.CreateScopeFactory();
        var dispatcher = new WebhookDispatcher(factory, new WebhookPayloadBuilder());
        var worker = new WebhookRetryWorker(dispatcher, factory, NullLogger<WebhookRetryWorker>.Instance);
        await worker.ProcessDueAsync();
        await using var verify = fixture.CreateContext();
        var logs = await verify.WebhookDeliveryLogs.OrderBy(l => l.AttemptNumber).ToListAsync();
        Assert.Equal([1, 2], logs.Select(l => l.AttemptNumber));
        Assert.All(logs, log => { Assert.Null(log.NextRetryAt); Assert.True(log.ClaimedUntil > DateTime.UtcNow); });
        await worker.ProcessDueAsync();
        Assert.Equal(3, await verify.WebhookDeliveryLogs.SumAsync(l => l.AttemptNumber));
    }

    [Fact]
    public async Task RestartedWorkersDeliverCommittedPendingLogWithSignature()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        Guid logId;
        await using (var db = fixture.CreateContext())
        {
            var webhook = new Webhook { AppId = app.Id, Url = "https://example.test/hook", Secret = "signing-secret", Events = ["item.created"] };
            var log = new WebhookDeliveryLog { Webhook = webhook, EventType = "item.created", Payload = "{\"event\":\"item.created\"}", AttemptNumber = 0, NextRetryAt = DateTime.UtcNow.AddSeconds(-1) };
            db.WebhookDeliveryLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id;
        }
        var factory = fixture.CreateScopeFactory();
        var dispatcher = new WebhookDispatcher(factory, new WebhookPayloadBuilder());
        var handler = new CapturingHandler();
        var delivery = new WebhookDeliveryWorker(dispatcher, new WebhookPayloadBuilder(), factory,
            NullLogger<WebhookDeliveryWorker>.Instance, new FixedClientFactory(handler));
        var retry = new WebhookRetryWorker(dispatcher, factory, NullLogger<WebhookRetryWorker>.Instance);
        await delivery.StartAsync(CancellationToken.None);
        try
        {
            await retry.ProcessDueAsync();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                await using var verify = fixture.CreateContext();
                if (await verify.WebhookDeliveryLogs.AnyAsync(l => l.Id == logId && l.SucceededAt != null)) break;
                await Task.Delay(20);
            }
            await using var final = fixture.CreateContext();
            var saved = await final.WebhookDeliveryLogs.SingleAsync(l => l.Id == logId);
            Assert.NotNull(saved.SucceededAt);
            Assert.Equal(1, saved.AttemptNumber);
            Assert.Equal(new WebhookPayloadBuilder().ComputeSignature("signing-secret", saved.Payload), handler.Signature);
        }
        finally { await delivery.StopAsync(CancellationToken.None); }
    }

    private sealed class FixedClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Signature { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Signature = request.Headers.GetValues("X-Extatic-Signature").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
