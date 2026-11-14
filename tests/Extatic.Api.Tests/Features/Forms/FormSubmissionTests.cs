using System.Text;
using System.Text.Json;
using Extatic.Api.Features.Forms;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Forms.FormsTestSupport;

namespace Extatic.Api.Tests.Features.Forms;

public class FormSubmissionTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private const string Json = "application/json";
    private const string UrlEncoded = "application/x-www-form-urlencoded";

    private const string ContactSchema = """
        {
          "type": "object",
          "required": ["name", "email"],
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "email": { "type": "string" },
            "age": { "type": "integer" },
            "budget": { "type": "number" },
            "subscribe": { "type": "boolean" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "counts": { "type": "array", "items": { "type": "integer" } }
          }
        }
        """;

    private static SubmissionAcceptedDto Accepted(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, obj.StatusCode);
        return Assert.IsType<SubmissionAcceptedDto>(obj.Value);
    }

    [Fact]
    public async Task JsonSubmit_StoresSubmission_StagesOneEmailPerAddress_AndWebhook()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        var webhook = await SeedWebhookAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var result = await ClientController(fixture, db, app, Json,
                """{"name":"Ada <b>","email":"ada@example.org","message":"Hi & bye"}""")
            .Submit(form.Slug, default);

        var accepted = Accepted(result);
        Assert.NotNull(accepted.Id);
        await using var verify = fixture.CreateContext();
        var saved = await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id);
        Assert.Equal(app.Id, saved.AppId);
        Assert.Equal(FormSubmissionStatus.New, saved.Status);
        Assert.Equal("TestAgent/1.0", saved.UserAgent);
        Assert.Equal("https://site.example.com/contact", saved.Referrer);
        Assert.Equal(64, saved.IpHash.Length);
        Assert.DoesNotContain("198.51.100.7", saved.IpHash);
        using (var data = JsonDocument.Parse(saved.Data))
            Assert.Equal("Ada <b>", data.RootElement.GetProperty("name").GetString());

        var emails = await verify.EmailOutbox.Where(m => m.AppId == app.Id).OrderBy(m => m.ToAddress).ToListAsync();
        Assert.Equal(["owner@example.com", "team@example.com"], emails.Select(e => e.ToAddress));
        Assert.All(emails, e =>
        {
            Assert.Equal("forms.notification", e.Category);
            Assert.Equal("ada@example.org", e.ReplyTo);
            Assert.Contains("Ada &lt;b&gt;", e.HtmlBody);
            Assert.Contains("Hi &amp; bye", e.HtmlBody);
            Assert.DoesNotContain("<b>", e.HtmlBody);
            Assert.Contains("message: Hi & bye", e.TextBody);
        });

        var log = await verify.WebhookDeliveryLogs.SingleAsync(l => l.WebhookId == webhook.Id);
        Assert.Equal("form.submitted", log.EventType);
        using var payload = JsonDocument.Parse(log.Payload);
        var payloadData = payload.RootElement.GetProperty("data");
        Assert.Equal(form.Slug, payloadData.GetProperty("form_slug").GetString());
        Assert.Equal(accepted.Id, payloadData.GetProperty("submission_id").GetGuid());
        Assert.Equal("Ada <b>", payloadData.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ReplyTo_IsOmittedWhenEmailFieldIsNotAValidAddress()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.NotificationEmails = ["owner@example.com"]);
        await using var db = fixture.CreateContext();

        Accepted(await ClientController(fixture, db, app, Json,
            """{"email":"x@y\r\nBcc: victim@example.com"}""").Submit(form.Slug, default));

        await using var verify = fixture.CreateContext();
        Assert.Null((await verify.EmailOutbox.SingleAsync(m => m.AppId == app.Id)).ReplyTo);
    }

    [Fact]
    public async Task UrlEncodedSubmit_ConvertsFields_RepeatedKeysBecomeArrays()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var accepted = Accepted(await ClientController(fixture, db, app, UrlEncoded,
            "name=Ada+Lovelace&topic=a&topic=b&single[]=x&note=caf%C3%A9").Submit(form.Slug, default));

        await using var verify = fixture.CreateContext();
        var saved = await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id);
        using var data = JsonDocument.Parse(saved.Data);
        Assert.Equal("Ada Lovelace", data.RootElement.GetProperty("name").GetString());
        Assert.Equal(["a", "b"], data.RootElement.GetProperty("topic").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["x"], data.RootElement.GetProperty("single").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("café", data.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task MultipartSubmit_AcceptsFields_RejectsFiles()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();
        const string boundary = "----x1234";

        var body = Multipart(boundary, ("name", "Grace", null), ("tag", "a", null), ("tag", "b", null),
            ("attachment", "", "")); // empty <input type=file> is ignored
        var accepted = Accepted(await ClientController(fixture, db, app,
            $"multipart/form-data; boundary={boundary}", body).Submit(form.Slug, default));
        await using (var verify = fixture.CreateContext())
        {
            using var data = JsonDocument.Parse((await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id)).Data);
            Assert.Equal("Grace", data.RootElement.GetProperty("name").GetString());
            Assert.Equal(2, data.RootElement.GetProperty("tag").GetArrayLength());
            Assert.False(data.RootElement.TryGetProperty("attachment", out _));
        }

        var withFile = Multipart(boundary, ("name", "Grace", null), ("cv", "file content", "cv.txt"));
        await using var db2 = fixture.CreateContext();
        await Assert.ThrowsAsync<BadRequestException>(() => ClientController(fixture, db2, app,
            $"multipart/form-data; boundary={boundary}", withFile).Submit(form.Slug, default));
    }

    [Theory]
    [InlineData("text/plain", "name=x")]
    [InlineData(Json, "[1,2]")]
    [InlineData(Json, "{not json")]
    public async Task UnsupportedOrMalformedBodies_Are400(string contentType, string body)
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            ClientController(fixture, db, app, contentType, body).Submit(form.Slug, default));
    }

    [Fact]
    public async Task UrlEncodedSubmit_CoercesStringsToSchemaTypes()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.Schema = ContactSchema);
        await using var db = fixture.CreateContext();

        var accepted = Accepted(await ClientController(fixture, db, app, UrlEncoded,
                "name=Ada&email=ada%40example.org&age=42&budget=9.5&subscribe=on&tags=solo&counts=1&counts=2&extra=7")
            .Submit(form.Slug, default));

        await using var verify = fixture.CreateContext();
        using var data = JsonDocument.Parse((await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id)).Data);
        var root = data.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("age").ValueKind);
        Assert.Equal(42, root.GetProperty("age").GetInt32());
        Assert.Equal(9.5m, root.GetProperty("budget").GetDecimal());
        Assert.True(root.GetProperty("subscribe").GetBoolean());
        Assert.Equal(["solo"], root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([1, 2], root.GetProperty("counts").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal("7", root.GetProperty("extra").GetString()); // not in the schema: left as a string
    }

    [Fact]
    public async Task Coercion_DropsEmptyOptionalNumbers_AndLeavesBadValuesForValidation()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.Schema = ContactSchema);
        await using var db = fixture.CreateContext();
        var accepted = Accepted(await ClientController(fixture, db, app, UrlEncoded,
            "name=Ada&email=a%40example.org&age=").Submit(form.Slug, default));
        await using (var verify = fixture.CreateContext())
        {
            using var data = JsonDocument.Parse((await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id)).Data);
            Assert.False(data.RootElement.TryGetProperty("age", out _));
        }

        await using var db2 = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => ClientController(fixture, db2, app, UrlEncoded,
            "name=Ada&email=a%40example.org&age=forty").Submit(form.Slug, default));
        Assert.Contains(ex.Errors, e => e.Pointer.Contains("age"));
    }

    [Fact]
    public async Task JsonBodies_AreNotCoerced()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.Schema = ContactSchema);
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ValidationException>(() => ClientController(fixture, db, app, Json,
            """{"name":"Ada","email":"a@example.org","age":"42"}""").Submit(form.Slug, default));
    }

    [Fact]
    public async Task SchemaFailure_Is422_WithFieldErrors_AndNothingIsStaged()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.Schema = ContactSchema);
        await SeedWebhookAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => ClientController(fixture, db, app, Json,
            """{"name":""}""").Submit(form.Slug, default));
        Assert.NotEmpty(ex.Errors);

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.FormSubmissions.AnyAsync(s => s.AppId == app.Id));
        Assert.False(await verify.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
        Assert.False(await verify.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task SubmissionEmailAndWebhook_AreAtomic_FailureRollsBackEverything()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        // Bypass save-time validation to make the second email fail to stage after the submission row was written.
        var form = await SeedFormAsync(fixture, app.Id, f => f.NotificationEmails = ["ok@example.com", "not-an-address"]);
        await SeedWebhookAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        await Assert.ThrowsAsync<ValidationException>(() => ClientController(fixture, db, app, Json,
            """{"name":"x"}""").Submit(form.Slug, default));

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.FormSubmissions.AnyAsync(s => s.AppId == app.Id));
        Assert.False(await verify.EmailOutbox.AnyAsync(m => m.AppId == app.Id));
        Assert.False(await verify.WebhookDeliveryLogs.AnyAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task Honeypot_LooksLikeSuccess_StoredAsSpam_NoEmailOrWebhook()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f =>
        {
            f.HoneypotField = "website";
            f.Schema = ContactSchema; // spam is not validated, so its 422 can't reveal the trap
            f.SuccessRedirectUrl = "https://site.example.com/thanks";
        });
        await SeedWebhookAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var spam = await ClientController(fixture, db, app, UrlEncoded, "name=bot&website=http%3A%2F%2Fspam")
            .Submit(form.Slug, default);
        var redirect = Assert.IsType<StatusCodeResult>(spam);
        Assert.Equal(303, redirect.StatusCode);

        await using var db2 = fixture.CreateContext();
        var spamJson = Accepted(await ClientController(fixture, db2, app, Json,
            """{"name":"bot","website":"x"}""").Submit(form.Slug, default));
        Assert.NotNull(spamJson.Id);

        // An empty honeypot is a normal submission, and the honeypot key is not stored.
        await using var db3 = fixture.CreateContext();
        var ok = Accepted(await ClientController(fixture, db3, app, Json,
            """{"name":"Ada","email":"a@example.org","website":""}""").Submit(form.Slug, default));

        await using var verify = fixture.CreateContext();
        var rows = await verify.FormSubmissions.Where(s => s.AppId == app.Id).ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Status == FormSubmissionStatus.Spam));
        var good = rows.Single(r => r.Id == ok.Id);
        Assert.Equal(FormSubmissionStatus.New, good.Status);
        Assert.DoesNotContain("website", good.Data);
        Assert.Equal(2, await verify.EmailOutbox.CountAsync(m => m.AppId == app.Id)); // only for the real one
        Assert.Equal(1, await verify.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task StoreSubmissionsOff_StillNotifies_ReturnsNullId()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.StoreSubmissions = false);
        await SeedWebhookAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var accepted = Accepted(await ClientController(fixture, db, app, Json, """{"a":1}""").Submit(form.Slug, default));

        Assert.Null(accepted.Id);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.FormSubmissions.AnyAsync(s => s.AppId == app.Id));
        Assert.Equal(2, await verify.EmailOutbox.CountAsync(m => m.AppId == app.Id));
        Assert.Equal(1, await verify.WebhookDeliveryLogs.CountAsync(l => l.Webhook.AppId == app.Id));
    }

    [Fact]
    public async Task InactiveUnknownAndForeignForms_Are404()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var inactive = await SeedFormAsync(fixture, app.Id, f => f.IsActive = false);
        var foreign = await SeedFormAsync(fixture, otherApp.Id);
        await using var db = fixture.CreateContext();

        foreach (var slug in new[] { inactive.Slug, "missing", foreign.Slug })
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                ClientController(fixture, db, app, Json, "{}").Submit(slug, default));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                ClientController(fixture, db, app, Json, "{}").Get(slug));
        }
    }

    [Fact]
    public async Task PublicDefinition_ExposesSchemaButNotSettings()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => { f.Schema = ContactSchema; f.RequireAppUser = true; f.HoneypotField = "trap"; });
        await using var db = fixture.CreateContext();

        var result = await ClientController(fixture, db, app, Json, "").Get(form.Slug);

        var dto = Assert.IsType<PublicFormDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(form.Slug, dto.Slug);
        Assert.True(dto.RequireAppUser);
        Assert.Equal("object", dto.Schema!.Value.GetProperty("type").GetString());
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("owner@example.com", json);
        Assert.DoesNotContain("trap", json);
    }

    [Fact]
    public async Task RequireAppUser_AnonymousOrForeignToken401_SignedInStoresAppUser()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, otherApp, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.RequireAppUser = true);
        var user = await fixture.SeedAppUserAsync(app.Id);
        await using var db = fixture.CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ClientController(fixture, db, app, Json, "{}").Submit(form.Slug, default));
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ClientController(fixture, db, app, Json, "{}", appUserId: user.Id, tokenAppId: otherApp.Id).Submit(form.Slug, default));

        var accepted = Accepted(await ClientController(fixture, db, app, Json, "{}", appUserId: user.Id).Submit(form.Slug, default));
        await using var verify = fixture.CreateContext();
        Assert.Equal(user.Id, (await verify.FormSubmissions.SingleAsync(s => s.Id == accepted.Id)).AppUserId);
    }

    [Fact]
    public async Task HtmlPost_RedirectsToConfiguredUrl_JsonAndAcceptJsonGet201()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id, f => f.SuccessRedirectUrl = "https://site.example.com/thanks");
        var plain = await SeedFormAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();

        var html = await ClientController(fixture, db, app, UrlEncoded, "a=1&redirect=https%3A%2F%2Fevil.example")
            .Submit(form.Slug, default);
        var controller = ClientController(fixture, db, app, UrlEncoded, "a=1");
        var again = await controller.Submit(form.Slug, default);
        Assert.Equal(303, Assert.IsType<StatusCodeResult>(html).StatusCode);
        Assert.Equal(303, Assert.IsType<StatusCodeResult>(again).StatusCode);
        Assert.Equal("https://site.example.com/thanks", controller.Response.Headers.Location.ToString());

        Accepted(await ClientController(fixture, db, app, Json, """{"a":1}""").Submit(form.Slug, default));
        Accepted(await ClientController(fixture, db, app, UrlEncoded, "a=1", accept: "application/json").Submit(form.Slug, default));
        Accepted(await ClientController(fixture, db, app, UrlEncoded, "a=1").Submit(plain.Slug, default));
    }

    [Fact]
    public async Task PayloadOver64KB_Is413()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var form = await SeedFormAsync(fixture, app.Id);
        await using var db = fixture.CreateContext();
        var big = $$"""{"message":"{{new string('x', 64 * 1024)}}"}""";

        await Assert.ThrowsAsync<FileTooLargeException>(() =>
            ClientController(fixture, db, app, Json, big).Submit(form.Slug, default));

        // Without a Content-Length (chunked) the streamed read still stops at the limit.
        var chunked = ClientController(fixture, db, app, Json, big);
        chunked.Request.ContentLength = null;
        await Assert.ThrowsAsync<FileTooLargeException>(() => chunked.Submit(form.Slug, default));

        // Just under the limit is fine.
        var ok = $$"""{"message":"{{new string('x', 64 * 1024 - 20)}}"}""";
        Assert.True(Encoding.UTF8.GetByteCount(ok) <= 64 * 1024);
        Accepted(await ClientController(fixture, db, app, Json, ok).Submit(form.Slug, default));
    }

    [Fact]
    public void Notification_HtmlEncodesKeysAndValues()
    {
        var form = new Form { Name = "<script>x</script>", Slug = "f" };
        var data = System.Text.Json.Nodes.JsonNode.Parse("""{"<img src=x>":"<a href='y'>z</a>"}""")!.AsObject();
        var message = FormSubmissionService.BuildNotification(form, data, "to@example.com");
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.DoesNotContain("<img", message.HtmlBody);
        Assert.DoesNotContain("<a href", message.HtmlBody);
        Assert.Contains("&lt;img src=x&gt;", message.HtmlBody);
    }

    [Fact]
    public void Module_DeclaresFormSubmittedEvent()
        => Assert.Contains("form.submitted", new FormsModule().WebhookEvents);
}
