using System.Text.Json;
using Extatic.Api.Features.Comments;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Comments;

public class ClientCommentTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly CommentTestKit kit = new(fixture);

    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static T Value<T>(ActionResult<T> result) where T : class
        => result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    [Theory]
    [InlineData(CommentModerationMode.All, false, CommentStatus.Pending)]
    [InlineData(CommentModerationMode.All, true, CommentStatus.Pending)]
    [InlineData(CommentModerationMode.AnonymousOnly, false, CommentStatus.Pending)]
    [InlineData(CommentModerationMode.AnonymousOnly, true, CommentStatus.Approved)]
    [InlineData(CommentModerationMode.None, false, CommentStatus.Approved)]
    [InlineData(CommentModerationMode.None, true, CommentStatus.Approved)]
    public async Task ModerationMode_DecidesInitialStatus(CommentModerationMode mode, bool signedIn, CommentStatus expected)
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = mode);
        var user = signedIn ? await kit.SeedAppUserAsync(app.Id, "Jane Doe") : null;
        var comment = await kit.CreateAsync(app, signedIn ? CommentTestKit.Signed() : CommentTestKit.Anon(), user?.Id);
        Assert.Equal(expected, comment.Status);
        if (signedIn)
        {
            Assert.Equal("Jane Doe", comment.AuthorName);
            Assert.Equal(user!.Email, comment.AuthorEmail);
            Assert.Equal(user.Id, comment.AppUserId);
        }
    }

    [Theory]
    [InlineData(BlockedTermAction.Pending, CommentStatus.Pending)]
    [InlineData(BlockedTermAction.Spam, CommentStatus.Spam)]
    public async Task BlockedTerms_Downgrade(BlockedTermAction action, CommentStatus expected)
    {
        var (_, app) = await kit.SeedAsync(s =>
        {
            s.ModerationMode = CommentModerationMode.None;
            s.BlockedTerms = ["casino"];
            s.BlockedTermAction = action;
        });
        Assert.Equal(expected, (await kit.CreateAsync(app, CommentTestKit.Anon("Visit my CASINO now"))).Status);
        Assert.Equal(CommentStatus.Approved, (await kit.CreateAsync(app, CommentTestKit.Anon("Lovely post"))).Status);
    }

    [Fact]
    public async Task LinkLimit_DowngradesToPending()
    {
        var (_, app) = await kit.SeedAsync(s => { s.ModerationMode = CommentModerationMode.None; s.MaxLinks = 2; });
        Assert.Equal(CommentStatus.Approved,
            (await kit.CreateAsync(app, CommentTestKit.Anon("see https://a.example and www.b.example"))).Status);
        Assert.Equal(CommentStatus.Pending,
            (await kit.CreateAsync(app, CommentTestKit.Anon("http://a.example https://b.example www.c.example"))).Status);
    }

    [Fact]
    public async Task Honeypot_PretendsSuccess_StoresSpam_NoNotifications()
    {
        var (_, app) = await kit.SeedAsync(s =>
        {
            s.ModerationMode = CommentModerationMode.None;
            s.NotifyEmails = ["mod@example.com"];
        }, withWebhook: true);
        await using var db = fixture.CreateContext();
        var result = await kit.Client(db, app).Create(CommentTestKit.Anon(website: "http://spam.example"));
        var dto = Value(result);
        Assert.Equal(201, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("pending", dto.Status);

        var stored = await kit.FindAsync(dto.Id);
        Assert.Equal(CommentStatus.Spam, stored!.Status);
        Assert.Empty(await kit.EmailCategoriesAsync(app.Id));
        Assert.Empty(await kit.WebhookEventsAsync(app.Id));
    }

    [Fact]
    public async Task OnlyApprovedArePublic_SignedInSeesOwnPending()
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.All);
        var user = await kit.SeedAppUserAsync(app.Id);
        var approved = await kit.InsertAsync(app, CommentStatus.Approved, body: "visible");
        await kit.InsertAsync(app, CommentStatus.Rejected, body: "rejected");
        await kit.InsertAsync(app, CommentStatus.Spam, body: "spam");
        await kit.InsertAsync(app, CommentStatus.Pending, body: "someone else pending");
        var own = await kit.CreateAsync(app, CommentTestKit.Signed("my pending"), user.Id);
        Assert.Equal(CommentStatus.Pending, own.Status);

        await using var db = fixture.CreateContext();
        var anon = Value(await kit.Client(db, app).List("/blog/hello"));
        Assert.Equal(1, anon.Total);
        Assert.Equal([approved.Id], anon.Items.Select(i => i.Id));

        var mine = Value(await kit.Client(db, app, user.Id).List("/blog/hello"));
        Assert.Equal(1, mine.Total);
        Assert.Equal(2, mine.Items.Count);
        var pending = Assert.Single(mine.Items, i => i.Pending);
        Assert.Equal(own.Id, pending.Id);
        Assert.True(pending.IsOwn);
        Assert.DoesNotContain(mine.Items, i => i.Body is "rejected" or "spam" or "someone else pending");
    }

    [Fact]
    public async Task AuthorEmail_NeverInClientResponses()
    {
        var (_, app) = await kit.SeedAsync(s =>
        {
            s.ModerationMode = CommentModerationMode.None;
            s.ExposeAvatarHash = true;
        });
        var user = await kit.SeedAppUserAsync(app.Id);
        await using var db = fixture.CreateContext();
        var anonPost = Value(await kit.Client(db, app).Create(CommentTestKit.Anon(email: "secret-anon@example.com")));
        var signedPost = Value(await kit.Client(db, app, user.Id).Create(CommentTestKit.Signed(parentId: anonPost.Id)));
        var edited = Value(await kit.Client(db, app, user.Id).Update(signedPost.Id, new UpdateCommentRequest("edited")));
        var thread = Value(await kit.Client(db, app, user.Id).List("/blog/hello"));
        var counts = Value(await kit.Client(db, app).Counts("/blog/hello"));

        Assert.NotNull(thread.Items[0].AvatarHash);
        Assert.Single(thread.Items[0].Replies);
        foreach (var payload in new object[] { anonPost, signedPost, edited, thread, counts })
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType(), ApiJson);
            Assert.DoesNotContain("secret-anon@example.com", json);
            Assert.DoesNotContain(user.Email!, json);
            Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AvatarHash_OnlyWhenExposed()
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        await kit.CreateAsync(app, CommentTestKit.Anon());
        await using var db = fixture.CreateContext();
        var thread = Value(await kit.Client(db, app).List("/blog/hello"));
        Assert.Null(thread.Items[0].AvatarHash);
    }

    [Fact]
    public async Task Threading_NestsReplies_AndEnforcesMaxDepth()
    {
        var (_, app) = await kit.SeedAsync(s => { s.ModerationMode = CommentModerationMode.None; s.MaxDepth = 2; });
        var root = await kit.CreateAsync(app, CommentTestKit.Anon("root"));
        var reply = await kit.CreateAsync(app, CommentTestKit.Anon("reply", parentId: root.Id));
        Assert.Equal(2, reply.Depth);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CreateAsync(app, CommentTestKit.Anon("too deep", parentId: reply.Id)));
        Assert.Contains(ex.Errors, e => e.Pointer == "parent_id");

        var second = await kit.CreateAsync(app, CommentTestKit.Anon("second root"));
        await using var db = fixture.CreateContext();
        var newest = Value(await kit.Client(db, app).List("/blog/hello", sort: "newest"));
        Assert.Equal(2, newest.Total);
        Assert.Equal(3, newest.ApprovedCount);
        Assert.Equal([second.Id, root.Id], newest.Items.Select(i => i.Id));
        Assert.Equal(reply.Id, Assert.Single(newest.Items[1].Replies).Id);

        var paged = Value(await kit.Client(db, app).List("/blog/hello", page: 2, page_size: 1));
        Assert.Equal(second.Id, Assert.Single(paged.Items).Id);
    }

    [Fact]
    public async Task Parent_FromOtherPageOrApp_OrNotApproved_IsRejected()
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        var (_, other) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        var otherPage = await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/other");
        var otherApp = await kit.InsertAsync(other, CommentStatus.Approved);
        var pending = await kit.InsertAsync(app, CommentStatus.Pending);

        foreach (var parentId in new[] { otherPage.Id, otherApp.Id, pending.Id, Guid.NewGuid() })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                kit.CreateAsync(app, CommentTestKit.Anon(parentId: parentId)));
            Assert.Contains(ex.Errors, e => e.Pointer == "parent_id");
        }
        await using var db = fixture.CreateContext();
        Assert.Equal(0, await db.Comments.CountAsync(c => c.AppId == app.Id && c.ParentId != null));
    }

    [Fact]
    public async Task Anonymous_Validation()
    {
        var (_, app) = await kit.SeedAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => kit.CreateAsync(app,
            new CreateCommentRequest("", null, null, null, null, null, "bad\u0007body", null)));
        Assert.Contains(ex.Errors, e => e.Pointer == "page_key");
        Assert.Contains(ex.Errors, e => e.Pointer == "body");
        Assert.Contains(ex.Errors, e => e.Pointer == "author_name");
        Assert.Contains(ex.Errors, e => e.Pointer == "author_email");

        ex = await Assert.ThrowsAsync<ValidationException>(() => kit.CreateAsync(app,
            CommentTestKit.Anon(body: new string('x', 5001))));
        Assert.Contains(ex.Errors, e => e.Pointer == "body");

        // Multi-line plain text is fine and stored verbatim (no HTML processing).
        var ok = await kit.CreateAsync(app, CommentTestKit.Anon("<script>alert(1)</script>\nline two"));
        Assert.Equal("<script>alert(1)</script>\nline two", ok.Body);

        var (_, signedOnly) = await kit.SeedAsync(s => s.AllowAnonymous = false);
        await Assert.ThrowsAsync<UnauthorizedException>(() => kit.CreateAsync(signedOnly, CommentTestKit.Anon()));

        var (_, noEmail) = await kit.SeedAsync(s => s.RequireEmailForAnonymous = false);
        Assert.Null((await kit.CreateAsync(noEmail, CommentTestKit.Anon(email: null))).AuthorEmail);
    }

    [Fact]
    public async Task Edit_OwnOnly_WithinWindow_RerunsModeration()
    {
        var (_, app) = await kit.SeedAsync(s =>
        {
            s.ModerationMode = CommentModerationMode.None;
            s.BlockedTerms = ["badword"];
        });
        var author = await kit.SeedAppUserAsync(app.Id);
        var stranger = await kit.SeedAppUserAsync(app.Id);
        var comment = await kit.CreateAsync(app, CommentTestKit.Signed("first"), author.Id);

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            kit.Client(db, app, stranger.Id).Update(comment.Id, new UpdateCommentRequest("hijack")));
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            kit.Client(db, app).Update(comment.Id, new UpdateCommentRequest("anon")));

        var edited = Value(await kit.Client(db, app, author.Id).Update(comment.Id, new UpdateCommentRequest("now with badword")));
        Assert.Equal("pending", edited.Status);
        Assert.NotNull(edited.EditedAt);

        var old = await kit.InsertAsync(app, CommentStatus.Approved, appUserId: author.Id,
            createdAt: DateTime.UtcNow.AddMinutes(-16));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            kit.Client(db, app, author.Id).Update(old.Id, new UpdateCommentRequest("late")));

        var (_, other) = await kit.SeedAsync();
        var foreign = await kit.InsertAsync(other, CommentStatus.Approved, appUserId: null);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            kit.Client(db, app, author.Id).Update(foreign.Id, new UpdateCommentRequest("x")));
    }

    [Fact]
    public async Task Delete_TombstonesWhenReplied_HardDeletesOtherwise()
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        var author = await kit.SeedAppUserAsync(app.Id);
        var stranger = await kit.SeedAppUserAsync(app.Id);
        var parent = await kit.CreateAsync(app, CommentTestKit.Signed("parent body"), author.Id);
        var reply = await kit.CreateAsync(app, CommentTestKit.Anon("reply", parentId: parent.Id));
        var lone = await kit.CreateAsync(app, CommentTestKit.Signed("lone"), author.Id);

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ForbiddenException>(() => kit.Client(db, app, stranger.Id).Delete(parent.Id));
        Assert.IsType<NoContentResult>(await kit.Client(db, app, author.Id).Delete(parent.Id));
        Assert.IsType<NoContentResult>(await kit.Client(db, app, author.Id).Delete(lone.Id));

        var tomb = await kit.FindAsync(parent.Id);
        Assert.True(tomb!.IsDeleted);
        Assert.Equal(string.Empty, tomb.Body);
        Assert.Null(await kit.FindAsync(lone.Id));

        var thread = Value(await kit.Client(db, app).List("/blog/hello"));
        var shown = Assert.Single(thread.Items);
        Assert.True(shown.IsDeleted);
        Assert.Null(shown.Body);
        Assert.Null(shown.AuthorName);
        Assert.Equal(reply.Id, Assert.Single(shown.Replies).Id);
        Assert.Equal(1, thread.ApprovedCount);

        await Assert.ThrowsAsync<NotFoundException>(() => kit.Client(db, app, author.Id).Delete(parent.Id));
    }

    [Fact]
    public async Task ClosedPages_AutoClose_AndDisabled_Reject()
    {
        var (owner, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        await using (var db = fixture.CreateContext())
            await kit.Moderation(db).SetPageClosedAsync(app.Id, "/blog/hello", closed: true);
        await Assert.ThrowsAsync<ForbiddenException>(() => kit.CreateAsync(app, CommentTestKit.Anon()));
        await using (var db = fixture.CreateContext())
        {
            Assert.True(Value(await kit.Client(db, app).List("/blog/hello")).IsClosed);
            await kit.Moderation(db).SetPageClosedAsync(app.Id, "/blog/hello", closed: false);
        }
        await kit.CreateAsync(app, CommentTestKit.Anon());

        var (_, auto) = await kit.SeedAsync(s => s.AutoCloseAfterDays = 7);
        await kit.InsertAsync(auto, CommentStatus.Approved, createdAt: DateTime.UtcNow.AddDays(-8));
        await Assert.ThrowsAsync<ForbiddenException>(() => kit.CreateAsync(auto, CommentTestKit.Anon()));
        await kit.CreateAsync(auto, CommentTestKit.Anon(pageKey: "/fresh"));

        var (_, disabled) = await kit.SeedAsync(s => s.Enabled = false);
        await Assert.ThrowsAsync<ForbiddenException>(() => kit.CreateAsync(disabled, CommentTestKit.Anon()));
        await using (var db = fixture.CreateContext())
        {
            var thread = Value(await kit.Client(db, disabled).List("/blog/hello"));
            Assert.False(thread.Enabled);
        }

        // An App that never saved settings has comments disabled.
        var (_, fresh, _) = await fixture.SeedAppAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => kit.CreateAsync(fresh, CommentTestKit.Anon()));
        _ = owner;
    }

    [Fact]
    public async Task Counts_ApprovedOnly_MaxFiftyKeys()
    {
        var (_, app) = await kit.SeedAsync();
        await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/a");
        await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/a");
        await kit.InsertAsync(app, CommentStatus.Pending, pageKey: "/a");
        await kit.InsertAsync(app, CommentStatus.Approved, pageKey: "/b");
        var (_, other) = await kit.SeedAsync();
        await kit.InsertAsync(other, CommentStatus.Approved, pageKey: "/a");

        await using var db = fixture.CreateContext();
        var counts = Value(await kit.Client(db, app).Counts("/a, /b,/c"));
        Assert.Equal(2, counts.Counts["/a"]);
        Assert.Equal(1, counts.Counts["/b"]);
        Assert.Equal(0, counts.Counts["/c"]);
        var tooMany = string.Join(',', Enumerable.Range(0, 51).Select(i => $"/p{i}"));
        await Assert.ThrowsAsync<BadRequestException>(() => kit.Client(db, app).Counts(tooMany));
    }

    [Fact]
    public async Task Notifications_StagedAtomically_WithComment()
    {
        var (_, app) = await kit.SeedAsync(s =>
        {
            s.ModerationMode = CommentModerationMode.AnonymousOnly;
            s.NotifyEmails = ["mod1@example.com", "mod2@example.com"];
        }, withWebhook: true, allowedOrigins: ["https://site.example"]);
        var parentAuthor = await kit.SeedAppUserAsync(app.Id);
        var replier = await kit.SeedAppUserAsync(app.Id, "Replier");

        var parent = await kit.CreateAsync(app, CommentTestKit.Signed("parent <b>bold</b>"), parentAuthor.Id);
        var emails = await kit.EmailsAsync(app.Id);
        Assert.Equal(2, emails.Count(e => e.Category == "comments.new"));
        Assert.All(emails, e => Assert.Contains("parent &lt;b&gt;bold&lt;/b&gt;", e.Html));
        Assert.Equal(["comment.created"], await kit.WebhookEventsAsync(app.Id));

        // Own reply does not email yourself; a different signed-in replier does.
        await kit.CreateAsync(app, CommentTestKit.Signed("self reply", parentId: parent.Id), parentAuthor.Id);
        Assert.DoesNotContain(await kit.EmailsAsync(app.Id), e => e.Category == "comments.reply");
        var req = CommentTestKit.Signed("hi there", parentId: parent.Id) with { PageUrl = "https://site.example/blog/hello" };
        await kit.CreateAsync(app, req, replier.Id);
        var reply = Assert.Single(await kit.EmailsAsync(app.Id), e => e.Category == "comments.reply");
        Assert.Equal(parentAuthor.Email, reply.To);
        Assert.Contains("https://site.example/blog/hello", reply.Html);

        // A failed write stages nothing.
        var before = (await kit.EmailsAsync(app.Id)).Count;
        await Assert.ThrowsAsync<ValidationException>(() =>
            kit.CreateAsync(app, CommentTestKit.Anon(parentId: Guid.NewGuid())));
        Assert.Equal(before, (await kit.EmailsAsync(app.Id)).Count);
        Assert.Equal(3, (await kit.WebhookEventsAsync(app.Id)).Count);
    }

    [Fact]
    public void PageUrl_FromForeignOrigin_IsNotLinkedInEmails()
    {
        var app = new Extatic.Api.Domain.Entities.App { AllowedOrigins = ["https://site.example"] };
        Assert.Null(CommentNotifier.SafePageUrl(app, "https://evil.example/login"));
        Assert.Null(CommentNotifier.SafePageUrl(app, "javascript:alert(1)"));
        Assert.Equal("https://site.example/a", CommentNotifier.SafePageUrl(app, "https://site.example/a"));
    }

    [Fact]
    public async Task ConcurrentReplyAndDelete_ThreadStaysConsistent()
    {
        var (_, app) = await kit.SeedAsync(s => s.ModerationMode = CommentModerationMode.None);
        var author = await kit.SeedAppUserAsync(app.Id);
        for (var round = 0; round < 5; round++)
        {
            var parent = await kit.CreateAsync(app, CommentTestKit.Signed($"p{round}"), author.Id);
            var replyTask = Task.Run(async () =>
            {
                try { return (Guid?)(await kit.CreateAsync(app, CommentTestKit.Anon("r", parentId: parent.Id))).Id; }
                catch (ValidationException) { return null; }
            });
            var deleteTask = Task.Run(async () =>
            {
                await using var db = fixture.CreateContext();
                await kit.Service(db).DeleteAsync(app, author.Id, parent.Id);
            });
            await Task.WhenAll(replyTask, deleteTask);

            var stored = await kit.FindAsync(parent.Id);
            if (await replyTask is Guid replyId)
            {
                Assert.NotNull(await kit.FindAsync(replyId));
                Assert.True(stored!.IsDeleted); // tombstone kept for the reply
            }
            else
            {
                Assert.Null(stored);
            }
        }
    }
}
