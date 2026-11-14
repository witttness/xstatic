using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Forms;

/// <summary>Platform-side management of Forms and their submissions. Every query is scoped by AppId.</summary>
public class FormService(AppDbContext db)
{
    public const int MaxExportRows = 50_000;

    // ---- Forms ----

    public async Task<List<(Form Form, FormCounts Counts)>> ListAsync(Guid appId)
    {
        var forms = await db.Forms.AsNoTracking()
            .Where(f => f.AppId == appId)
            .OrderBy(f => f.Name).ThenBy(f => f.Slug)
            .ToListAsync();
        var counts = await CountsAsync(appId, null);
        return forms.Select(f => (f, counts.GetValueOrDefault(f.Id, FormCounts.Empty))).ToList();
    }

    public async Task<Form> GetAsync(Guid appId, Guid formId)
        => await db.Forms.FirstOrDefaultAsync(f => f.AppId == appId && f.Id == formId)
           ?? throw new NotFoundException("Form not found");

    public async Task<FormCounts> GetCountsAsync(Guid appId, Guid formId)
        => (await CountsAsync(appId, formId)).GetValueOrDefault(formId, FormCounts.Empty);

    public async Task<Form> CreateAsync(Guid appId, SaveFormRequest req)
    {
        var valid = FormRules.Validate(req);
        var form = new Form { AppId = appId };
        Apply(form, valid, req);
        db.Forms.Add(form);
        await SaveUniqueAsync(valid.Slug);
        return form;
    }

    public async Task<Form> UpdateAsync(Guid appId, Guid formId, SaveFormRequest req)
    {
        var form = await GetAsync(appId, formId);
        var valid = FormRules.Validate(req);
        Apply(form, valid, req);
        await SaveUniqueAsync(valid.Slug);
        return form;
    }

    public async Task DeleteAsync(Guid appId, Guid formId)
    {
        var form = await GetAsync(appId, formId);
        db.Forms.Remove(form);
        await db.SaveChangesAsync(); // submissions cascade in the database
    }

    private static void Apply(Form form, FormRules.ValidatedForm valid, SaveFormRequest req)
    {
        form.Name = valid.Name;
        form.Slug = valid.Slug;
        form.Schema = valid.Schema;
        form.NotificationEmails = valid.NotificationEmails;
        form.SuccessRedirectUrl = valid.SuccessRedirectUrl;
        form.HoneypotField = valid.HoneypotField;
        form.IsActive = req.IsActive;
        form.StoreSubmissions = req.StoreSubmissions;
        form.RequireAppUser = req.RequireAppUser;
    }

    /// <summary>The (AppId, Slug) unique index is the source of truth, so concurrent creates cannot both win.</summary>
    private async Task SaveUniqueAsync(string slug)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            throw new ConflictException($"A form with slug '{slug}' already exists in this app");
        }
    }

    private async Task<Dictionary<Guid, FormCounts>> CountsAsync(Guid appId, Guid? formId)
    {
        var query = db.FormSubmissions.Where(s => s.AppId == appId);
        if (formId is not null) query = query.Where(s => s.FormId == formId);
        var rows = await query
            .GroupBy(s => new { s.FormId, s.Status })
            .Select(g => new { g.Key.FormId, g.Key.Status, Count = g.Count() })
            .ToListAsync();
        return rows.GroupBy(r => r.FormId).ToDictionary(
            g => g.Key,
            g => new FormCounts(
                g.Where(r => r.Status != FormSubmissionStatus.Spam).Sum(r => r.Count),
                g.Where(r => r.Status == FormSubmissionStatus.New).Sum(r => r.Count),
                g.Where(r => r.Status == FormSubmissionStatus.Spam).Sum(r => r.Count)));
    }

    // ---- Submissions ----

    public static FormSubmissionStatus? ParseStatus(string? status, string pointer = "status")
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        if (Enum.TryParse<FormSubmissionStatus>(status.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed) && !int.TryParse(status, out _))
            return parsed;
        throw new ValidationException("Invalid status",
            [new ValidationError(pointer, "Must be one of: new, read, spam, archived")]);
    }

    private IQueryable<FormSubmission> Filtered(Guid appId, Guid formId, FormSubmissionStatus? status,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var query = db.FormSubmissions.Where(s => s.AppId == appId && s.FormId == formId);
        if (status is not null) query = query.Where(s => s.Status == status);
        if (from is not null)
        {
            var f = from.Value.UtcDateTime;
            query = query.Where(s => s.CreatedAt >= f);
        }
        if (to is not null)
        {
            var t = to.Value.UtcDateTime;
            query = query.Where(s => s.CreatedAt <= t);
        }
        return query;
    }

    public async Task<(List<FormSubmission> Items, int Total)> ListSubmissionsAsync(Guid appId, Guid formId,
        string? status, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var parsedStatus = ParseStatus(status);
        await GetAsync(appId, formId);
        var query = Filtered(appId, formId, parsedStatus, from, to);
        var total = await query.CountAsync();
        var items = await query.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<FormSubmission> GetSubmissionAsync(Guid appId, Guid formId, Guid submissionId)
        => await db.FormSubmissions.FirstOrDefaultAsync(s =>
               s.AppId == appId && s.FormId == formId && s.Id == submissionId)
           ?? throw new NotFoundException("Submission not found");

    public async Task<FormSubmission> UpdateStatusAsync(Guid appId, Guid formId, Guid submissionId, string? status)
    {
        var parsed = ParseStatus(status)
            ?? throw new ValidationException("Invalid status", [new ValidationError("status", "Status is required")]);
        var submission = await GetSubmissionAsync(appId, formId, submissionId);
        submission.Status = parsed;
        await db.SaveChangesAsync();
        return submission;
    }

    public async Task DeleteSubmissionAsync(Guid appId, Guid formId, Guid submissionId)
    {
        var submission = await GetSubmissionAsync(appId, formId, submissionId);
        db.FormSubmissions.Remove(submission);
        await db.SaveChangesAsync();
    }

    public async Task<int> DeleteSpamAsync(Guid appId, Guid formId)
    {
        await GetAsync(appId, formId);
        return await db.FormSubmissions
            .Where(s => s.AppId == appId && s.FormId == formId && s.Status == FormSubmissionStatus.Spam)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// CSV of the matching submissions (oldest first, at most <see cref="MaxExportRows"/>). Columns: metadata,
    /// then the union of top-level data keys in order of first appearance. Formula-like cells are neutralised by CsvBuilder.
    /// </summary>
    public async Task<byte[]> ExportCsvAsync(Guid appId, Guid formId, string? status,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var parsedStatus = ParseStatus(status);
        await GetAsync(appId, formId);
        var rows = await Filtered(appId, formId, parsedStatus, from, to).AsNoTracking()
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Take(MaxExportRows)
            .ToListAsync();

        var parsed = new List<(FormSubmission Row, JsonDocument Doc)>(rows.Count);
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var row in rows)
            {
                var doc = JsonDocument.Parse(row.Data);
                parsed.Add((row, doc));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in doc.RootElement.EnumerateObject())
                    if (seen.Add(property.Name)) keys.Add(property.Name);
            }

            string[] metadata = ["submission_id", "submitted_at", "status", "app_user_id", "referrer", "user_agent"];
            var csv = new CsvBuilder();
            csv.AddRow(metadata.Concat(keys));
            foreach (var (row, doc) in parsed)
            {
                var cells = new List<object?>
                {
                    row.Id, row.CreatedAt, row.Status.ToApiString(), row.AppUserId, row.Referrer, row.UserAgent
                };
                foreach (var key in keys)
                {
                    cells.Add(doc.RootElement.ValueKind == JsonValueKind.Object
                              && doc.RootElement.TryGetProperty(key, out var value)
                        ? CellValue(value)
                        : null);
                }
                csv.AddRow(cells);
            }
            return csv.ToBytes();
        }
        finally
        {
            foreach (var (_, doc) in parsed) doc.Dispose();
        }
    }

    private static string? CellValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array when value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
            => string.Join("; ", value.EnumerateArray().Select(e => e.GetString())),
        _ => value.GetRawText()
    };
}
