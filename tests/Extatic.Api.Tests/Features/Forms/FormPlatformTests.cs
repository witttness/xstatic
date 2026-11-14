using System.Reflection;
using System.Text;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Features.Forms;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Forms.FormsTestSupport;

namespace Extatic.Api.Tests.Features.Forms;

public class FormPlatformTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static SaveFormRequest Request(string slug = "contact", string? redirect = null, string[]? emails = null,
        string? schema = null, string? honeypot = null)
        => new("Contact", slug, schema is null ? null : JsonDocument.Parse(schema).RootElement.Clone(),
            emails ?? ["owner@example.com"], redirect, honeypot);

    private FormsController Forms(App app, User owner, Extatic.Api.Data.AppDbContext db)
        => new(new FormService(db)) { ControllerContext = TestContexts.Platform(app, owner.Id) };

    private FormSubmissionsController Subs(App app, User owner, Extatic.Api.Data.AppDbContext db)
        => new(new FormService(db)) { ControllerContext = TestContexts.Platform(app, owner.Id) };

    private static T Value<T>(ActionResult<T> result) where T : class
        => (result.Value ?? (result.Result as ObjectResult)?.Value as T)!;

    private async Task<FormSubmission> SeedSubmissionAsync(Guid appId, Guid formId, string data,
        FormSubmissionStatus status = FormSubmissionStatus.New, DateTime? createdAt = null)
    {
        await using var db = fixture.CreateContext();
        var s = new FormSubmission { AppId = appId, FormId = formId, Data = data, IpHash = new string('a', 64), Status = status };
        db.FormSubmissions.Add(s);
        await db.SaveChangesAsync();
        if (createdAt is not null)
            await db.FormSubmissions.Where(x => x.Id == s.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, createdAt.Value));
        return s;
    }

    [Fact]
    public async Task Crud_RoundTrips_WithDerivedCounts()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = Forms(app, owner, db);

        var created = Value(await controller.Create(Request(schema: """{"type":"object"}""", honeypot: "website",
            emails: ["a@example.com", "A@example.com", "b@example.com"])));
        Assert.Equal("contact", created.Slug);
        Assert.Equal(["a@example.com", "b@example.com"], created.NotificationEmails);
        Assert.True(created.IsActive);
        Assert.True(created.StoreSubmissions);
        Assert.False(created.RequireAppUser);

        await SeedSubmissionAsync(app.Id, created.Id, "{}");
        await SeedSubmissionAsync(app.Id, created.Id, "{}", FormSubmissionStatus.Read);
        await SeedSubmissionAsync(app.Id, created.Id, "{}", FormSubmissionStatus.Spam);

        var list = Value(await controller.List());
        var item = Assert.Single(list);
        Assert.Equal(2, item.SubmissionCount);
        Assert.Equal(1, item.UnreadCount);
        Assert.Equal(1, item.SpamCount);

        var updated = Value(await controller.Update(created.Id, Request("contact-2") with { IsActive = false, RequireAppUser = true }));
        Assert.Equal("contact-2", updated.Slug);
        Assert.False(updated.IsActive);
        Assert.Null(updated.Schema);
        Assert.Equal(2, updated.SubmissionCount);

        Assert.IsType<NoContentResult>(await controller.Delete(created.Id));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Forms.AnyAsync(f => f.AppId == app.Id));
        Assert.False(await verify.FormSubmissions.AnyAsync(s => s.AppId == app.Id));
    }

    [Theory]
    [InlineData("Bad Slug", null, null, "slug")]
    [InlineData("ok", "http://example.com/thanks", null, "success_redirect_url")]
    [InlineData("ok", "javascript:alert(1)", null, "success_redirect_url")]
    [InlineData("ok", "//evil.example/x", null, "success_redirect_url")]
    [InlineData("ok", "/thanks", null, "success_redirect_url")]
    [InlineData("ok", "https://user:pw@example.com/", null, "success_redirect_url")]
    [InlineData("ok", null, "{\"type\": 12}", "schema")]
    [InlineData("ok", null, "{\"$ref\": \"https://nowhere.example/s.json\"}", "schema")]
    [InlineData("ok", null, "[1]", "schema")]
    public async Task Create_ValidatesSettings(string slug, string? redirect, string? schema, string pointer)
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Forms(app, owner, db).Create(Request(slug, redirect, schema: schema)));
        Assert.Contains(ex.Errors, e => e.Pointer == pointer);
    }

    [Fact]
    public async Task Create_ValidatesEmailsAndHoneypot()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = Forms(app, owner, db);

        var tooMany = Enumerable.Range(0, 11).Select(i => $"u{i}@example.com").ToArray();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => controller.Create(Request(emails: tooMany)));
        Assert.Contains(ex.Errors, e => e.Pointer == "notification_emails");
        ex = await Assert.ThrowsAsync<ValidationException>(() => controller.Create(Request(emails: ["ok@example.com", "Name <x@example.com>"])));
        Assert.Contains(ex.Errors, e => e.Pointer == "notification_emails/1");
        ex = await Assert.ThrowsAsync<ValidationException>(() => controller.Create(Request(honeypot: "bad field!")));
        Assert.Contains(ex.Errors, e => e.Pointer == "honeypot_field");
    }

    [Theory]
    [InlineData("https://site.example.com/thanks?x=1")]
    [InlineData("http://localhost:4200/thanks")]
    [InlineData("http://127.0.0.1/thanks")]
    public void RedirectRules_AllowHttpsAndLocalhost(string url) => Assert.True(FormRules.IsAllowedRedirectUrl(url));

    [Fact]
    public async Task DuplicateSlug_Is409_EvenUnderConcurrentCreates()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            try
            {
                await Forms(app, owner, db).Create(Request("same"));
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        }).ToList();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r));
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Forms.CountAsync(f => f.AppId == app.Id && f.Slug == "same"));

        // Slug reuse is per app.
        var (owner2, app2, _) = await fixture.SeedAppAsync();
        await using var db2 = fixture.CreateContext();
        Assert.NotNull(Value(await Forms(app2, owner2, db2).Create(Request("same"))));
        // Renaming onto an existing slug also conflicts.
        var other = Value(await Forms(app, owner, db2).Create(Request("other")));
        await Assert.ThrowsAsync<ConflictException>(() => Forms(app, owner, db2).Update(other.Id, Request("same")));
    }

    [Fact]
    public async Task EveryPlatformEndpoint_Returns404ForAnotherAppsFormOrSubmission()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var mine = await SeedFormAsync(fixture, app.Id);
        var foreign = await SeedFormAsync(fixture, otherApp.Id);
        var foreignSub = await SeedSubmissionAsync(otherApp.Id, foreign.Id, "{}", FormSubmissionStatus.Spam);
        await using var db = fixture.CreateContext();
        var forms = Forms(app, owner, db);
        var subs = Subs(app, owner, db);

        await Assert.ThrowsAsync<NotFoundException>(() => forms.Get(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => forms.Update(foreign.Id, Request("hijack")));
        await Assert.ThrowsAsync<NotFoundException>(() => forms.Delete(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => subs.List(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => subs.Export(foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => subs.DeleteSpam(foreign.Id));
        foreach (var formId in new[] { foreign.Id, mine.Id })
        {
            await Assert.ThrowsAsync<NotFoundException>(() => subs.Get(formId, foreignSub.Id));
            await Assert.ThrowsAsync<NotFoundException>(() => subs.UpdateStatus(formId, foreignSub.Id, new("read")));
            await Assert.ThrowsAsync<NotFoundException>(() => subs.Delete(formId, foreignSub.Id));
        }
        Assert.DoesNotContain(Value(await forms.List()), f => f.Id == foreign.Id);

        await using var verify = fixture.CreateContext();
        var stillThere = await verify.FormSubmissions.SingleAsync(s => s.Id == foreignSub.Id);
        Assert.Equal(FormSubmissionStatus.Spam, stillThere.Status);
        Assert.Equal(foreign.Slug, (await verify.Forms.SingleAsync(f => f.Id == foreign.Id)).Slug);
    }

    [Fact]
    public async Task Submissions_ListFiltersStatusAndDates_PatchStatus_DeleteAndBulkDeleteSpam()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        var old = await SeedSubmissionAsync(app.Id, form.Id, """{"n":1}""", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = await SeedSubmissionAsync(app.Id, form.Id, """{"n":2}""");
        await SeedSubmissionAsync(app.Id, form.Id, """{"n":3}""", FormSubmissionStatus.Spam);
        await SeedSubmissionAsync(app.Id, form.Id, """{"n":4}""", FormSubmissionStatus.Spam);
        await using var db = fixture.CreateContext();
        var subs = Subs(app, owner, db);

        var all = Value(await subs.List(form.Id));
        Assert.Equal(4, all.Total);
        var spam = Value(await subs.List(form.Id, status: "spam"));
        Assert.Equal(2, spam.Total);
        Assert.All(spam.Items, s => Assert.Equal("spam", s.Status));
        var since = Value(await subs.List(form.Id, from: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.DoesNotContain(since.Items, s => s.Id == old.Id);
        var until = Value(await subs.List(form.Id, to: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(old.Id, Assert.Single(until.Items).Id);
        var paged = Value(await subs.List(form.Id, page: 2, page_size: 3));
        Assert.Single(paged.Items);
        await Assert.ThrowsAsync<ValidationException>(() => subs.List(form.Id, status: "bogus"));
        await Assert.ThrowsAsync<BadRequestException>(() => subs.List(form.Id, page_size: 101));

        var patched = Value(await subs.UpdateStatus(form.Id, recent.Id, new("read")));
        Assert.Equal("read", patched.Status);
        await Assert.ThrowsAsync<ValidationException>(() => subs.UpdateStatus(form.Id, recent.Id, new("1")));
        await Assert.ThrowsAsync<ValidationException>(() => subs.UpdateStatus(form.Id, recent.Id, new(null)));

        var deleted = Value(await subs.DeleteSpam(form.Id));
        Assert.Equal(2, deleted.Deleted);
        Assert.IsType<NoContentResult>(await subs.Delete(form.Id, old.Id));

        await using var verify = fixture.CreateContext();
        var remaining = await verify.FormSubmissions.Where(s => s.FormId == form.Id).ToListAsync();
        Assert.Equal(recent.Id, Assert.Single(remaining).Id);
        Assert.Equal(FormSubmissionStatus.Read, remaining[0].Status);
    }

    [Fact]
    public async Task CsvExport_UnionOfKeys_NeutralisesFormulas()
    {
        var (owner, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        await SeedSubmissionAsync(app.Id, form.Id, """{"name":"Ada","note":"=HYPERLINK(\"http://evil\")"}""",
            createdAt: DateTime.UtcNow.AddMinutes(-2));
        await SeedSubmissionAsync(app.Id, form.Id, """{"name":"@SUM(1)","tags":["a","b"],"n":5,"obj":{"k":1}}""",
            createdAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedSubmissionAsync(app.Id, form.Id, """{"name":"spam"}""", FormSubmissionStatus.Spam);
        await using var db = fixture.CreateContext();

        var file = Assert.IsType<FileContentResult>(await Subs(app, owner, db).Export(form.Id));
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal($"{form.Slug}-submissions.csv", file.FileDownloadName);
        var lines = Encoding.UTF8.GetString(file.FileContents).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // Data columns: union of keys in order of first appearance (within a row, jsonb's key order).
        Assert.Equal("submission_id,submitted_at,status,app_user_id,referrer,user_agent,name,note,n,obj,tags", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", lines[1]);
        Assert.Contains(",'@SUM(1),", lines[2]);
        Assert.EndsWith(",5,\"{\"\"k\"\": 1}\",a; b", lines[2]); // jsonb normalises object spacing

        var spamOnly = Assert.IsType<FileContentResult>(await Subs(app, owner, db).Export(form.Id, status: "spam"));
        Assert.Equal(2, Encoding.UTF8.GetString(spamOnly.FileContents).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData(typeof(FormsController), nameof(FormsController.List), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(FormsController), nameof(FormsController.Get), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(FormsController), nameof(FormsController.Create), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(FormsController), nameof(FormsController.Update), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(FormsController), nameof(FormsController.Delete), PolicyNames.AppOwnerOrAdmin)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.List), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.Get), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.Export), PolicyNames.AppAnyAccess)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.UpdateStatus), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.Delete), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(FormSubmissionsController), nameof(FormSubmissionsController.DeleteSpam), PolicyNames.AppOwnerOrEditor)]
    [InlineData(typeof(ClientFormsController), nameof(ClientFormsController.Get), PolicyNames.ClientAnonymousOrAppUser)]
    [InlineData(typeof(ClientFormsController), nameof(ClientFormsController.Submit), PolicyNames.ClientAnonymousOrAppUser)]
    public void Endpoints_DeclareExpectedPolicy(Type controller, string action, string policy)
    {
        var classPolicies = controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
        var actionPolicies = controller.GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToList();
        // Class-level AppAnyAccess applies to every action; the strictest declared policy must be the expected one.
        var effective = actionPolicies.Count > 0 ? actionPolicies : classPolicies.ToList();
        Assert.Contains(policy, effective);
    }

    [Fact]
    public void SubmitEndpoint_UsesAnonymousWritesRateLimit()
    {
        var attr = typeof(ClientFormsController).GetMethod(nameof(ClientFormsController.Submit))!
            .GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
        Assert.Equal(RateLimitPolicyNames.ClientAnonymousWrites, attr!.PolicyName);
    }

    [Fact]
    public async Task ClientPolicy_AcceptsAnonymousForKnownApp()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{app.PublicId}/forms/contact/submissions");
        Assert.True((await pipeline.AuthorizeAsync(context, PolicyNames.ClientAnonymousOrAppUser)).Succeeded);
    }
}
