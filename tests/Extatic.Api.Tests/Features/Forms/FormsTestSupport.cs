using System.Net;
using System.Text;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Features.Forms;
using Extatic.Api.Tests.TestSupport;
using Extatic.Api.Validation;

namespace Extatic.Api.Tests.Features.Forms;

internal static class FormsTestSupport
{
    public static FormSubmissionService Submissions(ApiTestFixture fixture, AppDbContext db)
        => new(db, new JsonSchemaValidator(), TestServices.WebhookDispatcher(fixture), TestServices.EmailQueue());

    public static async Task<Form> SeedFormAsync(ApiTestFixture fixture, Guid appId, Action<Form>? configure = null)
    {
        await using var db = fixture.CreateContext();
        var form = new Form
        {
            AppId = appId,
            Name = "Contact",
            Slug = "contact-" + Guid.NewGuid().ToString("N")[..8],
            NotificationEmails = ["owner@example.com", "team@example.com"]
        };
        configure?.Invoke(form);
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        return form;
    }

    public static async Task<Webhook> SeedWebhookAsync(ApiTestFixture fixture, Guid appId, string evt = "form.submitted")
    {
        await using var db = fixture.CreateContext();
        var webhook = new Webhook { AppId = appId, Url = "https://hooks.example.com/x", Secret = "s", Events = [evt] };
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync();
        return webhook;
    }

    public static ClientFormsController ClientController(ApiTestFixture fixture, AppDbContext db, App app,
        string contentType, string body, Guid? appUserId = null, string? accept = null, Guid? tokenAppId = null)
        => ClientController(fixture, db, app, contentType, Encoding.UTF8.GetBytes(body), appUserId, accept, tokenAppId);

    public static ClientFormsController ClientController(ApiTestFixture fixture, AppDbContext db, App app,
        string contentType, byte[] body, Guid? appUserId = null, string? accept = null, Guid? tokenAppId = null)
    {
        var controller = new ClientFormsController(Submissions(fixture, db))
        {
            ControllerContext = TestContexts.Client(app, appUserId, IPAddress.Parse("198.51.100.7"), tokenAppId)
        };
        var request = controller.HttpContext.Request;
        request.Method = "POST";
        request.ContentType = contentType;
        request.Body = new MemoryStream(body);
        request.ContentLength = body.Length;
        request.Headers.UserAgent = "TestAgent/1.0";
        request.Headers.Referer = "https://site.example.com/contact";
        if (accept is not null) request.Headers.Accept = accept;
        return controller;
    }

    public static string Multipart(string boundary, params (string Name, string Value, string? FileName)[] parts)
    {
        var sb = new StringBuilder();
        foreach (var (name, value, fileName) in parts)
        {
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Disposition: form-data; name=\"").Append(name).Append('"');
            if (fileName is not null) sb.Append("; filename=\"").Append(fileName).Append('"');
            sb.Append("\r\n");
            if (fileName is not null) sb.Append("Content-Type: text/plain\r\n");
            sb.Append("\r\n").Append(value).Append("\r\n");
        }
        sb.Append("--").Append(boundary).Append("--\r\n");
        return sb.ToString();
    }
}
