using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extatic.Api.Tests.Foundation;

public class EmailOutboxTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static EmailMessage Message(string to = "person@example.com", string subject = "Hello")
        => new(to, subject, "Body", null, null, "test.category");

    [Fact]
    public async Task Stage_OnlyAddsToContext_SavedWithCallersSaveChanges()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();

        var staged = TestServices.EmailQueue().Stage(db, app.Id, Message());

        await using (var check = fixture.CreateContext())
            Assert.False(await check.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
        await db.SaveChangesAsync();
        await using var verify = fixture.CreateContext();
        var saved = await verify.EmailOutbox.SingleAsync(m => m.Id == staged.Id);
        Assert.Equal(EmailOutboxStatus.Pending, saved.Status);
        Assert.Equal("person@example.com", saved.ToAddress);
        Assert.Equal("test.category", saved.Category);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("Name <a@example.com>")]
    [InlineData("a@example.com\r\nBcc: victim@example.com")]
    [InlineData("")]
    public async Task Stage_RejectsInvalidAddresses(string to)
    {
        await using var db = fixture.CreateContext();
        Assert.Throws<ValidationException>(() => TestServices.EmailQueue().Stage(db, Guid.NewGuid(), Message(to)));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Stage_StripsNewlinesFromSubject()
    {
        await using var db = fixture.CreateContext();
        var staged = TestServices.EmailQueue().Stage(db, Guid.NewGuid(), Message(subject: "Hi\r\nBcc: x@example.com"));
        Assert.Equal("Hi Bcc: x@example.com", staged.Subject);
    }

    [Fact]
    public async Task Worker_DeliversDueMessages_WithAppDisplayName()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        Guid id;
        await using (var db = fixture.CreateContext())
        {
            var staged = TestServices.EmailQueue().Stage(db, app.Id, Message());
            await db.SaveChangesAsync();
            id = staged.Id; // database-generated: available after SaveChanges
        }
        var sender = new FakeEmailSender();
        var worker = new EmailDeliveryWorker(fixture.CreateScopeFactory(), sender, NullLogger<EmailDeliveryWorker>.Instance);

        await worker.ProcessDueAsync();

        var sent = Assert.Single(sender.Sent, e => e.To == "person@example.com" && e.FromDisplayName == $"{app.Name} via Extatic");
        Assert.Equal("Hello", sent.Subject);
        await using var verify = fixture.CreateContext();
        var saved = await verify.EmailOutbox.SingleAsync(m => m.Id == id);
        Assert.Equal(EmailOutboxStatus.Sent, saved.Status);
        Assert.NotNull(saved.SentAt);
        Assert.Equal(1, saved.Attempts);

        // Not re-sent.
        await worker.ProcessDueAsync();
        Assert.Single(sender.Sent, e => e.To == "person@example.com" && e.FromDisplayName == $"{app.Name} via Extatic");
    }

    [Fact]
    public async Task Worker_BacksOffThenFailsAfterFiveAttempts()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        Guid id;
        await using (var db = fixture.CreateContext())
        {
            var staged = TestServices.EmailQueue().Stage(db, app.Id, Message("backoff@example.com"));
            await db.SaveChangesAsync();
            id = staged.Id; // database-generated: available after SaveChanges
        }
        var sender = new FakeEmailSender(failFirst: int.MaxValue);
        var worker = new EmailDeliveryWorker(fixture.CreateScopeFactory(), sender, NullLogger<EmailDeliveryWorker>.Instance);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var before = DateTime.UtcNow;
            await worker.ProcessDueAsync();
            await using var verify = fixture.CreateContext();
            var row = await verify.EmailOutbox.SingleAsync(m => m.Id == id);
            Assert.Equal(attempt, row.Attempts);
            Assert.NotNull(row.LastError);
            if (attempt < 5)
            {
                Assert.Equal(EmailOutboxStatus.Pending, row.Status);
                var delay = EmailDeliveryWorker.RetryDelays[attempt - 1];
                Assert.InRange(row.NextAttemptAt!.Value, before.Add(delay).AddSeconds(-5), DateTime.UtcNow.Add(delay).AddSeconds(5));
                // Not due yet: another pass does nothing.
                await worker.ProcessDueAsync();
                await using var again = fixture.CreateContext();
                Assert.Equal(attempt, (await again.EmailOutbox.SingleAsync(m => m.Id == id)).Attempts);
                // Make it due again.
                await again.EmailOutbox.Where(m => m.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
            }
            else
            {
                Assert.Equal(EmailOutboxStatus.Failed, row.Status);
                Assert.Null(row.NextAttemptAt);
            }
        }
    }

    [Fact]
    public async Task Purge_RemovesOnlySentOlderThan30Days()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var queue = TestServices.EmailQueue();
        var old = queue.Stage(db, app.Id, Message("old@example.com"));
        var recent = queue.Stage(db, app.Id, Message("recent@example.com"));
        var pending = queue.Stage(db, app.Id, Message("pending@example.com"));
        old.Status = recent.Status = EmailOutboxStatus.Sent;
        old.SentAt = DateTime.UtcNow.AddDays(-31);
        recent.SentAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var worker = new EmailDeliveryWorker(fixture.CreateScopeFactory(), new FakeEmailSender(), NullLogger<EmailDeliveryWorker>.Instance);

        await worker.PurgeSentAsync();

        await using var verify = fixture.CreateContext();
        var remaining = await verify.EmailOutbox.Where(m => m.AppId == app.Id).Select(m => m.Id).ToListAsync();
        Assert.DoesNotContain(old.Id, remaining);
        Assert.Contains(recent.Id, remaining);
        Assert.Contains(pending.Id, remaining);
    }
}
