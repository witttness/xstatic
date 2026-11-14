using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Comments;

/// <summary>Client API (static site visitors) comment operations.</summary>
public class CommentService(AppDbContext db, CommentNotifier notifier)
{
    public async Task<CommentSettings> GetSettingsAsync(Guid appId)
        => await db.CommentSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AppId == appId)
           ?? CommentSettings.DefaultsFor(appId);

    /// <summary>Closed explicitly, or AutoCloseAfterDays have passed since the page's first (non-spam) comment.</summary>
    public async Task<bool> IsPageClosedAsync(Guid appId, string pageKey, CommentSettings settings)
    {
        if (await db.CommentPages.AnyAsync(p => p.AppId == appId && p.PageKey == pageKey && p.IsClosed))
            return true;
        if (settings.AutoCloseAfterDays is int days)
        {
            var first = await db.Comments
                .Where(c => c.AppId == appId && c.PageKey == pageKey && c.Status != CommentStatus.Spam)
                .MinAsync(c => (DateTime?)c.CreatedAt);
            if (first is not null && first.Value.AddDays(days) <= DateTime.UtcNow) return true;
        }
        return false;
    }

    /// <summary>Row-locks one comment of <paramref name="appId"/> (call inside a transaction).</summary>
    internal static async Task<Comment?> LockAsync(AppDbContext db, Guid appId, Guid id)
    {
        var rows = await db.Comments
            .FromSqlInterpolated($@"SELECT * FROM comments WHERE ""Id"" = {id} AND ""AppId"" = {appId} FOR UPDATE")
            .ToListAsync();
        return rows.FirstOrDefault();
    }

    // ---- Read ------------------------------------------------------------------------------------

    public async Task<CommentThreadDto> GetThreadAsync(Guid appId, Guid? viewerId, string? pageKeyInput,
        int page, int pageSize, string? sort)
    {
        var pageKey = CommentRules.RequirePageKey(pageKeyInput);
        Paging.Validate(page, pageSize);
        var newestFirst = sort?.Trim().ToLowerInvariant() switch
        {
            null or "" or "oldest" => false,
            "newest" => true,
            _ => throw new BadRequestException("sort must be 'oldest' or 'newest'")
        };

        var settings = await GetSettingsAsync(appId);
        if (!settings.Enabled)
            return new CommentThreadDto(pageKey, false, true, 0, 0, page, pageSize, []);

        var isClosed = await IsPageClosedAsync(appId, pageKey, settings);
        var onPage = db.Comments.AsNoTracking().Where(c => c.AppId == appId && c.PageKey == pageKey);
        var approvedTop = onPage.Where(c => c.ParentId == null && c.Status == CommentStatus.Approved);
        var total = await approvedTop.CountAsync();
        var approvedCount = await onPage.CountAsync(c => c.Status == CommentStatus.Approved && !c.IsDeleted);

        var ordered = newestFirst
            ? approvedTop.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            : approvedTop.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
        var top = await ordered.Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();

        // A signed-in viewer also sees their own Pending top-level comments (first page only).
        if (viewerId is not null && page == 1)
        {
            var ownPending = await onPage
                .Where(c => c.ParentId == null && c.Status == CommentStatus.Pending && c.AppUserId == viewerId)
                .OrderBy(c => c.CreatedAt).Take(50).ToListAsync();
            top = newestFirst ? [.. ownPending.AsEnumerable().Reverse(), .. top] : [.. top, .. ownPending];
        }

        // Load replies level by level (approved, plus the viewer's own pending).
        var all = new List<Comment>(top);
        var frontier = top.Select(c => c.Id).ToList();
        for (var level = 0; level < CommentRules.AbsoluteMaxDepth && frontier.Count > 0; level++)
        {
            var ids = frontier;
            var children = await db.Comments.AsNoTracking()
                .Where(c => c.AppId == appId && c.ParentId != null && ids.Contains(c.ParentId.Value)
                            && (c.Status == CommentStatus.Approved
                                || (viewerId != null && c.AppUserId == viewerId && c.Status == CommentStatus.Pending)))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .ToListAsync();
            all.AddRange(children);
            frontier = children.Select(c => c.Id).ToList();
        }

        var byParent = all.Where(c => c.ParentId != null).ToLookup(c => c.ParentId!.Value);
        PublicCommentDto Map(Comment c) => new(
            c.Id,
            c.ParentId,
            c.IsDeleted ? null : c.AuthorName,
            c.IsDeleted || !settings.ExposeAvatarHash ? null : CommentRules.AvatarHash(c.AuthorEmail),
            c.IsDeleted ? null : c.Body,
            c.IsDeleted,
            c.Status == CommentStatus.Pending,
            viewerId != null && c.AppUserId == viewerId,
            c.CreatedAt,
            c.EditedAt,
            byParent[c.Id].Select(Map).ToList());

        return new CommentThreadDto(pageKey, true, isClosed, total, approvedCount, page, pageSize,
            top.Select(Map).ToList());
    }

    public async Task<CommentCountsDto> CountsAsync(Guid appId, string? pageKeys)
    {
        var keys = (pageKeys ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0) throw new BadRequestException("page_keys is required");
        if (keys.Count > CommentRules.MaxCountKeys)
            throw new BadRequestException($"At most {CommentRules.MaxCountKeys} page_keys are allowed");
        if (keys.Any(k => k.Length > CommentRules.MaxPageKeyLength))
            throw new BadRequestException("page_keys entry is too long");

        var settings = await GetSettingsAsync(appId);
        var counts = keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        if (!settings.Enabled) return new CommentCountsDto(counts);

        var rows = await db.Comments
            .Where(c => c.AppId == appId && keys.Contains(c.PageKey)
                        && c.Status == CommentStatus.Approved && !c.IsDeleted)
            .GroupBy(c => c.PageKey)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        foreach (var row in rows) counts[row.Key] = row.Count;
        return new CommentCountsDto(counts);
    }

    // ---- Write -----------------------------------------------------------------------------------

    /// <summary>
    /// Creates a comment. Returns the stored comment and whether the honeypot was tripped (caller must
    /// pretend success in that case).
    /// </summary>
    public async Task<(Comment Comment, bool Honeypot)> CreateAsync(App app, Guid? appUserId,
        CreateCommentRequest req, string ipHash, string? userAgent)
    {
        var settings = await GetSettingsAsync(app.Id);
        if (!settings.Enabled) throw new ForbiddenException("Comments are disabled");

        AppUser? appUser = null;
        if (appUserId is not null)
        {
            appUser = await db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.AppId == app.Id && u.Id == appUserId)
                ?? throw new UnauthorizedException("Sign-in required");
        }
        else if (!settings.AllowAnonymous)
        {
            throw new UnauthorizedException("Sign in to comment");
        }

        var errors = new List<ValidationError>();
        var pageKey = CommentRules.NormalisePageKey(req.PageKey, errors);
        var body = CommentRules.ValidateBody(req.Body, settings.MaxLength, errors);
        var pageUrl = CommentRules.ValidatePageUrl(req.PageUrl, errors);
        var pageTitle = CommentRules.ValidateOptionalLine(req.PageTitle, CommentRules.MaxPageTitleLength, "page_title", errors);
        var suppliedName = CommentRules.ValidateOptionalLine(req.AuthorName, CommentRules.MaxAuthorNameLength, "author_name", errors);

        string authorName;
        string? authorEmail;
        if (appUser is not null)
        {
            var displayName = appUser.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(displayName) && displayName.Length > CommentRules.MaxAuthorNameLength)
                displayName = displayName[..CommentRules.MaxAuthorNameLength];
            authorName = suppliedName
                ?? (string.IsNullOrEmpty(displayName) || CommentRules.HasAnyControlChars(displayName) ? null : displayName)
                ?? "Anonymous";
            authorEmail = appUser.Email;
        }
        else
        {
            authorName = suppliedName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(req.AuthorName))
                errors.Add(new ValidationError("author_name", "author_name is required"));
            authorEmail = string.IsNullOrWhiteSpace(req.AuthorEmail) ? null : req.AuthorEmail.Trim();
            if (authorEmail is null && settings.RequireEmailForAnonymous)
                errors.Add(new ValidationError("author_email", "author_email is required"));
            else if (authorEmail is not null && !Notifications.EmailQueue.IsValidAddress(authorEmail))
                errors.Add(new ValidationError("author_email", "Must be a valid email address"));
        }
        if (errors.Count > 0) throw new ValidationException("Invalid comment", errors);

        if (await IsPageClosedAsync(app.Id, pageKey!, settings))
            throw new ForbiddenException("Comments are closed for this page");

        var honeypot = !string.IsNullOrWhiteSpace(req.Website);

        await using var tx = await db.Database.BeginTransactionAsync();
        Comment? parent = null;
        if (req.ParentId is Guid parentId)
        {
            // Lock the parent so a concurrent author delete cannot remove it under the reply.
            parent = await LockAsync(db, app.Id, parentId);
            if (parent is null || parent.PageKey != pageKey)
                throw new ValidationException("Invalid parent",
                    [new ValidationError("parent_id", "Parent comment not found on this page")]);
            if (parent.Status != CommentStatus.Approved || parent.IsDeleted)
                throw new ValidationException("Invalid parent",
                    [new ValidationError("parent_id", "Replies are only allowed to approved comments")]);
            if (parent.Depth >= Math.Min(settings.MaxDepth, CommentRules.AbsoluteMaxDepth))
                throw new ValidationException("Invalid parent",
                    [new ValidationError("parent_id", $"Replies are limited to {settings.MaxDepth} levels")]);
        }

        var status = honeypot
            ? CommentStatus.Spam
            : CommentRules.DecideStatus(settings, appUser is not null, body!, authorName);
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            PageKey = pageKey!,
            PageUrl = pageUrl,
            PageTitle = pageTitle,
            ParentId = parent?.Id,
            Depth = parent is null ? 1 : parent.Depth + 1,
            AppUserId = appUser?.Id,
            AuthorName = authorName,
            AuthorEmail = authorEmail,
            Body = body!,
            Status = status,
            IpHash = ipHash,
            UserAgent = Truncate(userAgent, CommentRules.MaxUserAgentLength),
            CreatedAt = DateTime.UtcNow
        };
        db.Comments.Add(comment);

        if (!honeypot && status is CommentStatus.Pending or CommentStatus.Approved)
        {
            await notifier.StageCreatedAsync(db, app, settings, comment);
            if (parent is not null) await notifier.StageReplyEmailAsync(db, app, comment, parent);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (comment, honeypot);
    }

    public async Task<Comment> UpdateAsync(App app, Guid appUserId, Guid id, UpdateCommentRequest req)
    {
        var settings = await GetSettingsAsync(app.Id);
        if (!settings.Enabled) throw new ForbiddenException("Comments are disabled");

        await using var tx = await db.Database.BeginTransactionAsync();
        var comment = await LockAsync(db, app.Id, id);
        if (comment is null || comment.IsDeleted) throw new NotFoundException($"Comment {id} not found");
        if (comment.AppUserId != appUserId) throw new ForbiddenException("You can only edit your own comments");
        if (comment.Status is CommentStatus.Rejected or CommentStatus.Spam)
            throw new ForbiddenException("This comment can no longer be edited");
        if (DateTime.UtcNow - comment.CreatedAt > CommentRules.EditWindow)
            throw new ForbiddenException("Comments can only be edited within 15 minutes of posting");

        var errors = new List<ValidationError>();
        var body = CommentRules.ValidateBody(req.Body, settings.MaxLength, errors);
        if (errors.Count > 0) throw new ValidationException("Invalid comment", errors);

        var wasApproved = comment.Status == CommentStatus.Approved;
        comment.Body = body!;
        comment.EditedAt = DateTime.UtcNow;
        comment.Status = CommentRules.DecideStatus(settings, signedIn: true, comment.Body, comment.AuthorName);

        await notifier.StageEventAsync(db, app.Id, CommentNotifier.EventUpdated, comment);
        if (!wasApproved && comment.Status == CommentStatus.Approved && comment.ParentId is Guid parentId)
        {
            var parent = await db.Comments.AsNoTracking()
                .FirstOrDefaultAsync(c => c.AppId == app.Id && c.Id == parentId);
            if (parent is not null) await notifier.StageReplyEmailAsync(db, app, comment, parent);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return comment;
    }

    /// <summary>Deletes the author's own comment; keeps a tombstone when it has replies.</summary>
    public async Task DeleteAsync(App app, Guid appUserId, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var comment = await LockAsync(db, app.Id, id);
        if (comment is null || comment.IsDeleted) throw new NotFoundException($"Comment {id} not found");
        if (comment.AppUserId != appUserId) throw new ForbiddenException("You can only delete your own comments");

        var hasReplies = await db.Comments.AnyAsync(c => c.AppId == app.Id && c.ParentId == comment.Id);
        if (hasReplies)
        {
            comment.Body = string.Empty;
            comment.IsDeleted = true;
        }
        else
        {
            db.Comments.Remove(comment);
        }
        comment.IsDeleted = true;
        await notifier.StageEventAsync(db, app.Id, CommentNotifier.EventDeleted, comment);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
