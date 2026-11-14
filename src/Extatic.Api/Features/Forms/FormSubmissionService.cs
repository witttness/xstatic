using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Extatic.Api.Data;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Forms;

/// <summary>Request metadata captured with a submission. <c>IpHash</c> comes from ClientIps.Hash.</summary>
public record SubmissionContext(Guid? AppUserId, string IpHash, string? UserAgent, string? Referrer);

/// <summary>Outcome of a public submission. Spam is reported to the caller exactly like success.</summary>
public record SubmissionResult(Guid? Id, DateTime CreatedAt, bool IsHtmlForm, string? RedirectUrl, bool IsSpam);

/// <summary>Public (Client API) side of forms: definition lookup and submission intake.</summary>
public class FormSubmissionService(
    AppDbContext db,
    JsonSchemaValidator validator,
    WebhookDispatcher webhookDispatcher,
    IEmailQueue emailQueue)
{
    public const string WebhookEvent = "form.submitted";
    public const string EmailCategory = "forms.notification";

    public async Task<Form> GetActiveAsync(Guid appId, string slug)
        => await db.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.AppId == appId && f.Slug == slug && f.IsActive)
           ?? throw new NotFoundException("Form not found");

    public async Task<SubmissionResult> SubmitAsync(Guid appId, string slug, string? contentType, byte[] body,
        SubmissionContext context)
    {
        if (body.Length > FormPayloadParser.MaxPayloadBytes)
            throw new FileTooLargeException($"Submission exceeds {FormPayloadParser.MaxPayloadBytes / 1024} KB");

        var form = await GetActiveAsync(appId, slug);
        if (form.RequireAppUser && context.AppUserId is null)
            throw new UnauthorizedException("Sign-in required to submit this form");

        var parsed = await FormPayloadParser.ParseAsync(contentType, body);
        var data = parsed.Data;

        // The honeypot is never stored or validated; a filled one marks the submission as spam.
        var isSpam = false;
        if (form.HoneypotField is { } honeypot && data.ContainsKey(honeypot))
        {
            isSpam = IsFilled(data[honeypot]);
            data.Remove(honeypot);
        }

        if (!isSpam && form.Schema is not null)
        {
            if (parsed.IsHtmlForm) FormPayloadParser.CoerceToSchema(data, form.Schema);
            var errors = validator.Validate(form.Schema, JsonSerializer.SerializeToElement(data));
            if (errors.Count > 0)
                throw new ValidationException("Submission does not match the form schema", errors);
        }

        var redirect = parsed.IsHtmlForm ? form.SuccessRedirectUrl : null;
        if (isSpam && !form.StoreSubmissions)
            return new SubmissionResult(null, DateTime.UtcNow, parsed.IsHtmlForm, redirect, true);

        var dataJson = data.ToJsonString();
        await using var transaction = await db.Database.BeginTransactionAsync();
        FormSubmission? submission = null;
        if (form.StoreSubmissions)
        {
            submission = new FormSubmission
            {
                AppId = appId,
                FormId = form.Id,
                Data = dataJson,
                AppUserId = context.AppUserId,
                IpHash = context.IpHash,
                UserAgent = Truncate(context.UserAgent, FormSubmission.MaxUserAgentLength),
                Referrer = Truncate(context.Referrer, FormSubmission.MaxReferrerLength),
                Status = isSpam ? FormSubmissionStatus.Spam : FormSubmissionStatus.New
            };
            db.FormSubmissions.Add(submission);
            await db.SaveChangesAsync(); // assigns Id + CreatedAt for the webhook payload
        }
        var createdAt = submission?.CreatedAt ?? DateTime.UtcNow;

        if (!isSpam)
        {
            foreach (var to in form.NotificationEmails)
                emailQueue.Stage(db, appId, BuildNotification(form, data, to));

            using var dataDoc = JsonDocument.Parse(dataJson);
            await webhookDispatcher.StageAsync(db, appId, WebhookEvent, JsonSerializer.SerializeToElement(new
            {
                form_id = form.Id,
                form_slug = form.Slug,
                submission_id = submission?.Id,
                created_at = createdAt,
                data = dataDoc.RootElement
            }));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();

        return new SubmissionResult(submission?.Id, createdAt, parsed.IsHtmlForm, redirect, isSpam);
    }

    private static bool IsFilled(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue<string>(out var s) => !string.IsNullOrWhiteSpace(s),
        JsonArray a => a.Any(IsFilled),
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        _ => true
    };

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;

    /// <summary>Text + HTML notification. All user content in the HTML body is HTML-encoded.</summary>
    public static EmailMessage BuildNotification(Form form, JsonObject data, string to)
    {
        var fields = data.Select(p => (Key: p.Key, Value: Display(p.Value))).ToList();

        var text = new StringBuilder();
        text.Append("New submission to \"").Append(form.Name).Append("\" (").Append(form.Slug).Append(")\n\n");
        foreach (var (key, value) in fields)
            text.Append(key).Append(": ").Append(value).Append('\n');
        if (fields.Count == 0) text.Append("(no fields)\n");

        var html = new StringBuilder();
        html.Append("<p>New submission to <strong>").Append(WebUtility.HtmlEncode(form.Name)).Append("</strong> (")
            .Append(WebUtility.HtmlEncode(form.Slug)).Append(")</p>");
        html.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse\">");
        foreach (var (key, value) in fields)
        {
            html.Append("<tr><th align=\"left\" valign=\"top\">").Append(WebUtility.HtmlEncode(key))
                .Append("</th><td style=\"white-space:pre-wrap\">").Append(WebUtility.HtmlEncode(value))
                .Append("</td></tr>");
        }
        html.Append("</table>");

        string? replyTo = null;
        if (data["email"] is JsonValue emailValue && emailValue.TryGetValue<string>(out var email)
            && EmailQueue.IsValidAddress(email))
            replyTo = email.Trim();

        return new EmailMessage(to, $"New submission: {form.Name}", text.ToString(), html.ToString(), replyTo,
            EmailCategory);
    }

    private static string Display(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray a when a.All(e => e is JsonValue ev && ev.TryGetValue<string>(out _))
            => string.Join(", ", a.Select(e => e!.GetValue<string>())),
        _ => node.ToJsonString()
    };
}
