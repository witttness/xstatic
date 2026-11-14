using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Comments;

/// <summary>Platform API (dashboard) comment moderation, settings and pages.</summary>
public class CommentModerationService(AppDbContext db, CommentNotifier notifier)
{
    // ---- Queue -----------------------------------------------------------------------------------

    public async Task<PagedResult<CommentAdminDto>> ListAsync(Guid appId, string? status, string? pageKey,
        string? search, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = db.Comments.AsNoTracking().Where(c => c.AppId == appId);
        CommentStatus? statusFilter = string.IsNullOrWhiteSpace(status)
            ? null
            : CommentMappings.ParseApi<CommentStatus>(status, "status");
        if (statusFilter is not null) query = query.Where(c => c.Status == statusFilter);
        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            var key = pageKey.Trim();
            query = query.Where(c => c.PageKey == key);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{CommentRules.EscapeLike(search.Trim())}%";
            query = query.Where(c => EF.Functions.ILike(c.Body, pattern, @"\")
                                     || EF.Functions.ILike(c.AuthorName, pattern, @"\")
                                     || (c.AuthorEmail != null && EF.Functions.ILike(c.AuthorEmail, pattern, @"\")));
        }

        var total = await query.CountAsync();
        var ordered = statusFilter is null
            ? query.OrderBy(c => c.Status == CommentStatus.Pending ? 0 : 1)
                   .ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            : query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id);
        var rows = await ordered.Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<CommentAdminDto>(rows.Select(c => c.ToAdminDto()).ToList(), total, page, pageSize);
    }

    public async Task<Comment> GetAsync(Guid appId, Guid id)
        => await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.AppId == appId && c.Id == id)
           ?? throw new NotFoundException($"Comment {id} not found");

    public static CommentStatus ParseAction(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "approve" => CommentStatus.Approved,
        "reject" => CommentStatus.Rejected,
        "spam" => CommentStatus.Spam,
        _ => throw new ValidationException("Invalid action",
            [new ValidationError("action", "Must be one of: approve, reject, spam")])
    };

    /// <summary>
    /// Moves the given comments (of <paramref name="app"/> only) to <paramref name="target"/>. Rows are
    /// locked so concurrent moderation of the same comment notifies once. Ids from other Apps are not found.
    /// </summary>
    public async Task<BulkModerateResult> ModerateAsync(App app, Guid userId, IReadOnlyCollection<Guid> ids,
        CommentStatus target)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0)
            throw new ValidationException("No comments", [new ValidationError("ids", "At least one id is required")]);
        if (distinct.Length > CommentRules.MaxBulk)
            throw new ValidationException("Too many comments",
                [new ValidationError("ids", $"At most {CommentRules.MaxBulk} ids per request")]);

        await using var tx = await db.Database.BeginTransactionAsync();
        var appId = app.Id;
        var comments = await db.Comments
            .FromSqlInterpolated(
                $@"SELECT * FROM comments WHERE ""AppId"" = {appId} AND ""Id"" = ANY({distinct}) ORDER BY ""Id"" FOR UPDATE")
            .ToListAsync();

        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        var updated = 0;
        foreach (var comment in comments.Where(c => c.Status != target))
        {
            comment.Status = target;
            comment.ModeratedAt = DateTime.UtcNow;
            comment.ModeratedByUserId = userExists ? userId : null;
            updated++;
            if (target == CommentStatus.Approved)
            {
                await notifier.StageEventAsync(db, appId, CommentNotifier.EventApproved, comment);
                if (comment.ParentId is Guid parentId)
                {
                    var parent = await db.Comments.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.AppId == appId && c.Id == parentId);
                    if (parent is not null) await notifier.StageReplyEmailAsync(db, app, comment, parent);
                }
            }
            else
            {
                await notifier.StageEventAsync(db, appId, CommentNotifier.EventRejected, comment);
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new BulkModerateResult(updated, distinct.Length - comments.Count);
    }

    /// <summary>Single-comment moderation; 404 when the comment is not in this App.</summary>
    public async Task<Comment> ModerateOneAsync(App app, Guid userId, Guid id, CommentStatus target)
    {
        var result = await ModerateAsync(app, userId, [id], target);
        if (result.NotFound > 0) throw new NotFoundException($"Comment {id} not found");
        return await GetAsync(app.Id, id);
    }

    /// <summary>Hard delete; replies are removed with it (FK cascade).</summary>
    public async Task DeleteAsync(Guid appId, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var comment = await CommentService.LockAsync(db, appId, id)
            ?? throw new NotFoundException($"Comment {id} not found");
        db.Comments.Remove(comment);
        comment.IsDeleted = true;
        await notifier.StageEventAsync(db, appId, CommentNotifier.EventDeleted, comment);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<CommentStatsDto> StatsAsync(Guid appId)
    {
        var rows = await db.Comments.Where(c => c.AppId == appId)
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        int Count(CommentStatus s) => rows.FirstOrDefault(r => r.Key == s)?.Count ?? 0;
        return new CommentStatsDto(Count(CommentStatus.Pending), Count(CommentStatus.Approved),
            Count(CommentStatus.Rejected), Count(CommentStatus.Spam));
    }

    // ---- Settings --------------------------------------------------------------------------------

    public async Task<CommentSettings> GetSettingsAsync(Guid appId)
        => await db.CommentSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AppId == appId)
           ?? CommentSettings.DefaultsFor(appId);

    public async Task<CommentSettings> UpdateSettingsAsync(Guid appId, UpdateCommentSettingsRequest req)
    {
        var errors = new List<ValidationError>();
        var mode = CommentMappings.ParseApi<CommentModerationMode>(req.ModerationMode, "moderation_mode");
        var action = CommentMappings.ParseApi<BlockedTermAction>(req.BlockedTermAction, "blocked_term_action");
        if (req.MaxLength is < 1 or > CommentRules.AbsoluteMaxLength)
            errors.Add(new ValidationError("max_length", $"Must be between 1 and {CommentRules.AbsoluteMaxLength}"));
        if (req.MaxDepth is < 1 or > CommentRules.AbsoluteMaxDepth)
            errors.Add(new ValidationError("max_depth", $"Must be between 1 and {CommentRules.AbsoluteMaxDepth}"));
        if (req.AutoCloseAfterDays is < 1 or > 3650)
            errors.Add(new ValidationError("auto_close_after_days", "Must be between 1 and 3650, or null"));
        if (req.MaxLinks is < 0 or > 100)
            errors.Add(new ValidationError("max_links", "Must be between 0 and 100"));

        var terms = (req.BlockedTerms ?? [])
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length > CommentRules.MaxBlockedTerms)
            errors.Add(new ValidationError("blocked_terms", $"At most {CommentRules.MaxBlockedTerms} terms"));
        if (terms.Any(t => t.Length > CommentRules.MaxBlockedTermLength || CommentRules.HasAnyControlChars(t)))
            errors.Add(new ValidationError("blocked_terms",
                $"Terms must be at most {CommentRules.MaxBlockedTermLength} characters without control characters"));

        var emails = (req.NotifyEmails ?? [])
            .Select(e => e?.Trim() ?? string.Empty)
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (emails.Length > CommentRules.MaxNotifyEmails)
            errors.Add(new ValidationError("notify_emails", $"At most {CommentRules.MaxNotifyEmails} addresses"));
        for (var i = 0; i < emails.Length; i++)
            if (!EmailQueue.IsValidAddress(emails[i]))
                errors.Add(new ValidationError($"notify_emails/{i}", "Must be a valid email address"));
        if (errors.Count > 0) throw new ValidationException("Invalid comment settings", errors);

        var settings = await GetOrCreateSettingsAsync(appId);
        settings.Enabled = req.Enabled;
        settings.ModerationMode = mode;
        settings.AllowAnonymous = req.AllowAnonymous;
        settings.RequireEmailForAnonymous = req.RequireEmailForAnonymous;
        settings.MaxLength = req.MaxLength;
        settings.MaxDepth = req.MaxDepth;
        settings.AutoCloseAfterDays = req.AutoCloseAfterDays;
        settings.BlockedTerms = terms;
        settings.BlockedTermAction = action;
        settings.MaxLinks = req.MaxLinks;
        settings.NotifyEmails = emails;
        settings.ExposeAvatarHash = req.ExposeAvatarHash;
        await db.SaveChangesAsync();
        return settings;
    }

    /// <summary>Tracked settings row, inserting the defaults first. Safe under concurrent first saves.</summary>
    private async Task<CommentSettings> GetOrCreateSettingsAsync(Guid appId)
    {
        var existing = await db.CommentSettings.FirstOrDefaultAsync(s => s.AppId == appId);
        if (existing is not null) return existing;
        var created = CommentSettings.DefaultsFor(appId);
        db.CommentSettings.Add(created);
        try
        {
            await db.SaveChangesAsync();
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(created).State = EntityState.Detached;
            return await db.CommentSettings.FirstAsync(s => s.AppId == appId);
        }
    }

    // ---- Pages -----------------------------------------------------------------------------------

    public async Task<PagedResult<CommentPageDto>> ListPagesAsync(Guid appId, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var grouped = db.Comments.Where(c => c.AppId == appId)
            .GroupBy(c => c.PageKey)
            .Select(g => new
            {
                PageKey = g.Key,
                PageUrl = g.Max(c => c.PageUrl),
                PageTitle = g.Max(c => c.PageTitle),
                Total = g.Count(),
                Approved = g.Count(c => c.Status == CommentStatus.Approved && !c.IsDeleted),
                Pending = g.Count(c => c.Status == CommentStatus.Pending),
                LastCommentAt = g.Max(c => c.CreatedAt)
            });
        var total = await grouped.CountAsync();
        var rows = await grouped.OrderByDescending(r => r.LastCommentAt).ThenBy(r => r.PageKey)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        var keys = rows.Select(r => r.PageKey).ToList();
        var closed = await db.CommentPages
            .Where(p => p.AppId == appId && p.IsClosed && keys.Contains(p.PageKey))
            .Select(p => p.PageKey).ToListAsync();
        var closedSet = closed.ToHashSet(StringComparer.Ordinal);
        var items = rows.Select(r => new CommentPageDto(r.PageKey, r.PageUrl, r.PageTitle,
            closedSet.Contains(r.PageKey), r.Total, r.Approved, r.Pending, r.LastCommentAt)).ToList();
        return new PagedResult<CommentPageDto>(items, total, page, pageSize);
    }

    /// <summary>Closes or reopens a page (upsert on (AppId, PageKey); safe under concurrent calls).</summary>
    public async Task<CommentPage> SetPageClosedAsync(Guid appId, string? pageKeyInput, bool closed)
    {
        var pageKey = CommentRules.RequirePageKey(pageKeyInput);
        var page = await db.CommentPages.FirstOrDefaultAsync(p => p.AppId == appId && p.PageKey == pageKey);
        if (page is null)
        {
            page = new CommentPage { AppId = appId, PageKey = pageKey, IsClosed = closed };
            db.CommentPages.Add(page);
            try
            {
                await db.SaveChangesAsync();
                return page;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.Entry(page).State = EntityState.Detached;
                page = await db.CommentPages.FirstAsync(p => p.AppId == appId && p.PageKey == pageKey);
            }
        }
        page.IsClosed = closed;
        await db.SaveChangesAsync();
        return page;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
