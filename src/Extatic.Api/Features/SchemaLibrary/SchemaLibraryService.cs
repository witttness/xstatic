using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>Which library a management call operates on: the global one, or one App's.</summary>
public readonly record struct LibraryScope(LibrarySchemaScope Scope, Guid? AppId)
{
    public static LibraryScope Global => new(LibrarySchemaScope.Global, null);
    public static LibraryScope ForApp(Guid appId) => new(LibrarySchemaScope.App, appId);
}

public record LibrarySchemaListQuery(
    string? Q = null,
    string? Category = null,
    string? Visibility = null,
    bool? Archived = null,
    int Page = 1,
    int PageSize = 50);

public class SchemaLibraryService(AppDbContext db, JsonSchemaValidator validator)
{
    // ---- management (global library or one App's library) ----

    public async Task<PagedResult<LibrarySchemaSummaryDto>> ListAsync(LibraryScope scope, LibrarySchemaListQuery q)
    {
        Paging.Validate(q.Page, q.PageSize);
        var query = InScope(db.LibrarySchemas.AsNoTracking(), scope);
        if (q.Archived is not null) query = query.Where(s => s.IsArchived == q.Archived.Value);
        if (!string.IsNullOrWhiteSpace(q.Visibility))
        {
            if (scope.Scope != LibrarySchemaScope.Global)
                throw new BadRequestException("visibility filter applies to the global library only");
            var visibility = LibrarySchemaRules.ParseVisibility(q.Visibility);
            query = query.Where(s => s.Visibility == visibility);
        }
        query = ApplyFilters(query, q.Q, q.Category);

        var total = await query.CountAsync();
        var rows = await query.OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Skip(Paging.Skip(q.Page, q.PageSize)).Take(q.PageSize).ToListAsync();
        return new PagedResult<LibrarySchemaSummaryDto>(rows.Select(s => s.ToSummaryDto()).ToList(), total, q.Page, q.PageSize);
    }

    public async Task<LibrarySchemaDto> GetAsync(LibraryScope scope, Guid id)
    {
        var schema = await FindAsync(scope, id);
        var versions = await db.LibrarySchemaVersions.AsNoTracking()
            .Where(v => v.LibrarySchemaId == id).ToListAsync();
        return schema.ToDto(versions);
    }

    public async Task<LibrarySchemaVersionDto> GetVersionAsync(LibraryScope scope, Guid id, int version)
    {
        await FindAsync(scope, id);
        var row = await db.LibrarySchemaVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.LibrarySchemaId == id && v.Version == version);
        return row?.ToDto() ?? throw new NotFoundException($"Version {version} not found");
    }

    public async Task<LibrarySchemaDto> CreateAsync(LibraryScope scope, CreateLibrarySchemaRequest req, Guid userId)
    {
        var name = LibrarySchemaRules.EnsureName(req.Name);
        Slugs.EnsureValid(req.Slug);
        LibrarySchemaRules.EnsureValidSchema(req.Schema);
        var schema = new LibrarySchema
        {
            Scope = scope.Scope,
            AppId = scope.AppId,
            Name = name,
            Slug = req.Slug,
            Description = LibrarySchemaRules.OptionalText(req.Description, LibrarySchemaRules.MaxDescriptionLength, "description"),
            Category = LibrarySchemaRules.OptionalText(req.Category, LibrarySchemaRules.MaxCategoryLength, "category"),
            Tags = LibrarySchemaRules.NormalizeTags(req.Tags),
            // Global schemas start as AdminOnly drafts unless stated; App schemas have no visibility.
            Visibility = scope.Scope == LibrarySchemaScope.Global
                ? (req.Visibility is null ? LibrarySchemaVisibility.AdminOnly : LibrarySchemaRules.ParseVisibility(req.Visibility))
                : null,
            Schema = req.Schema.GetRawText(),
            Version = 1,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
        };
        schema.Versions.Add(new LibrarySchemaVersion
        {
            AppId = scope.AppId,
            Version = 1,
            Schema = schema.Schema,
            ChangeNote = LibrarySchemaRules.OptionalText(req.ChangeNote, LibrarySchemaRules.MaxChangeNoteLength, "change_note"),
            CreatedByUserId = userId,
        });
        db.LibrarySchemas.Add(schema);
        await SaveHandlingSlugConflictAsync(req.Slug);
        return schema.ToDto(schema.Versions);
    }

    /// <summary>
    /// Metadata edits never bump the version; a content change (compared canonically) bumps it and
    /// writes a version row. The row lock serialises concurrent updates so version numbers are gap-
    /// and duplicate-free; the unique (schema, version) index is the backstop.
    /// </summary>
    public async Task<LibrarySchemaDto> UpdateAsync(LibraryScope scope, Guid id, UpdateLibrarySchemaRequest req, Guid userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var schema = await LockAsync(scope, id);

        if (req.Name is not null) schema.Name = LibrarySchemaRules.EnsureName(req.Name);
        if (req.Slug is not null && req.Slug != schema.Slug)
        {
            Slugs.EnsureValid(req.Slug);
            schema.Slug = req.Slug;
        }
        if (req.Description is not null)
            schema.Description = LibrarySchemaRules.OptionalText(req.Description, LibrarySchemaRules.MaxDescriptionLength, "description");
        if (req.Category is not null)
            schema.Category = LibrarySchemaRules.OptionalText(req.Category, LibrarySchemaRules.MaxCategoryLength, "category");
        if (req.Tags is not null) schema.Tags = LibrarySchemaRules.NormalizeTags(req.Tags);

        if (req.Schema is { } content)
        {
            LibrarySchemaRules.EnsureValidSchema(content);
            if (LibrarySchemaRules.Canonicalize(content) != LibrarySchemaRules.Canonicalize(schema.Schema))
            {
                schema.Version += 1;
                schema.Schema = content.GetRawText();
                db.LibrarySchemaVersions.Add(new LibrarySchemaVersion
                {
                    LibrarySchemaId = schema.Id,
                    AppId = schema.AppId,
                    Version = schema.Version,
                    Schema = schema.Schema,
                    ChangeNote = LibrarySchemaRules.OptionalText(req.ChangeNote, LibrarySchemaRules.MaxChangeNoteLength, "change_note"),
                    CreatedByUserId = userId,
                });
            }
        }

        schema.UpdatedByUserId = userId;
        await SaveHandlingSlugConflictAsync(schema.Slug);
        await tx.CommitAsync();
        return await GetAsync(scope, id);
    }

    public async Task<LibrarySchemaDto> SetVisibilityAsync(Guid id, string visibility, Guid userId)
    {
        var schema = await FindTrackedAsync(LibraryScope.Global, id);
        schema.Visibility = LibrarySchemaRules.ParseVisibility(visibility);
        schema.UpdatedByUserId = userId;
        await db.SaveChangesAsync();
        return await GetAsync(LibraryScope.Global, id);
    }

    public async Task<LibrarySchemaDto> SetArchivedAsync(LibraryScope scope, Guid id, bool archived, Guid userId)
    {
        var schema = await FindTrackedAsync(scope, id);
        schema.IsArchived = archived;
        schema.UpdatedByUserId = userId;
        await db.SaveChangesAsync();
        return await GetAsync(scope, id);
    }

    /// <summary>
    /// Collections keep their copied schema; their provenance (id and version) is cleared. The FK's
    /// ON DELETE SET NULL is the backstop for the id.
    /// </summary>
    public async Task DeleteAsync(LibraryScope scope, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var schema = await LockAsync(scope, id);
        var collections = db.Collections.Where(c => c.SourceSchemaId == schema.Id);
        if (scope.Scope == LibrarySchemaScope.App) collections = collections.Where(c => c.AppId == scope.AppId!.Value);
        await collections.ExecuteUpdateAsync(u => u
            .SetProperty(c => c.SourceSchemaId, (Guid?)null)
            .SetProperty(c => c.SourceSchemaVersion, (int?)null));
        db.LibrarySchemas.Remove(schema);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Collections whose provenance points at the schema (an App's own collections for App scope).</summary>
    public async Task<List<SchemaUsageDto>> UsageAsync(LibraryScope scope, Guid id)
    {
        await FindAsync(scope, id);
        var collections = db.Collections.AsNoTracking().Where(c => c.SourceSchemaId == id);
        if (scope.Scope == LibrarySchemaScope.App) collections = collections.Where(c => c.AppId == scope.AppId!.Value);
        return await collections
            .OrderBy(c => c.App.Slug).ThenBy(c => c.Slug)
            .Select(c => new SchemaUsageDto(c.AppId, c.App.Slug, c.App.Name, c.Id, c.Slug, c.Name, c.SourceSchemaVersion))
            .ToListAsync();
    }

    // ---- picker (what an App's collection editors may choose from) ----

    public async Task<PagedResult<AvailableSchemaDto>> ListAvailableAsync(Guid appId, bool isPlatformAdmin,
        string? q, string? category, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = db.LibrarySchemas.AsNoTracking().VisibleTo(appId, isPlatformAdmin).Where(s => !s.IsArchived);
        query = ApplyFilters(query, q, category);
        var total = await query.CountAsync();
        var rows = await query.OrderBy(s => s.Scope).ThenBy(s => s.Name).ThenBy(s => s.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<AvailableSchemaDto>(rows.Select(s => s.ToAvailableDto()).ToList(), total, page, pageSize);
    }

    /// <summary>404 for another App's schema and, for non-admins, for AdminOnly global schemas.</summary>
    public async Task<AvailableSchemaDetailDto> GetAvailableAsync(Guid appId, bool isPlatformAdmin, Guid id)
    {
        var schema = await db.LibrarySchemas.AsNoTracking().VisibleTo(appId, isPlatformAdmin)
            .FirstOrDefaultAsync(s => s.Id == id);
        return schema?.ToAvailableDetailDto() ?? throw new NotFoundException("Library schema not found");
    }

    public async Task<LibrarySchemaVersionDto> GetAvailableVersionAsync(Guid appId, bool isPlatformAdmin, Guid id, int version)
    {
        var visible = await db.LibrarySchemas.AsNoTracking().VisibleTo(appId, isPlatformAdmin).AnyAsync(s => s.Id == id);
        if (!visible) throw new NotFoundException("Library schema not found");
        var row = await db.LibrarySchemaVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.LibrarySchemaId == id && v.Version == version);
        return row?.ToDto() ?? throw new NotFoundException($"Version {version} not found");
    }

    // ---- validation ----

    public ValidateSchemaResponse Validate(ValidateSchemaRequest req)
    {
        var schemaErrors = LibrarySchemaRules.ValidateSchema(req.Schema);
        List<ValidationError>? sampleErrors = null;
        if (schemaErrors.Count == 0 && req.Sample is { } sample && sample.ValueKind != JsonValueKind.Undefined)
            sampleErrors = validator.Validate(req.Schema.GetRawText(), sample);
        return new ValidateSchemaResponse(schemaErrors.Count == 0 && (sampleErrors is null || sampleErrors.Count == 0),
            schemaErrors, sampleErrors);
    }

    // ---- helpers ----

    private static IQueryable<LibrarySchema> InScope(IQueryable<LibrarySchema> query, LibraryScope scope)
    {
        if (scope.Scope == LibrarySchemaScope.Global)
            return query.Where(s => s.Scope == LibrarySchemaScope.Global && s.AppId == null);
        var appId = scope.AppId ?? throw new InvalidOperationException("App scope requires an AppId");
        return query.Where(s => s.Scope == LibrarySchemaScope.App && s.AppId == appId);
    }

    private static IQueryable<LibrarySchema> ApplyFilters(IQueryable<LibrarySchema> query, string? q, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim().ToLower();
            query = query.Where(s => s.Category != null && s.Category.ToLower() == c);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var tag = term.ToLowerInvariant();
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern)
                || EF.Functions.ILike(s.Slug, pattern)
                || (s.Description != null && EF.Functions.ILike(s.Description, pattern))
                || (s.Category != null && EF.Functions.ILike(s.Category, pattern))
                || s.Tags.Contains(tag));
        }
        return query;
    }

    private async Task<LibrarySchema> FindAsync(LibraryScope scope, Guid id) =>
        await InScope(db.LibrarySchemas.AsNoTracking(), scope).FirstOrDefaultAsync(s => s.Id == id)
        ?? throw new NotFoundException("Library schema not found");

    private async Task<LibrarySchema> FindTrackedAsync(LibraryScope scope, Guid id) =>
        await InScope(db.LibrarySchemas, scope).FirstOrDefaultAsync(s => s.Id == id)
        ?? throw new NotFoundException("Library schema not found");

    /// <summary>Loads the row with <c>FOR UPDATE</c>; call inside a transaction.</summary>
    private async Task<LibrarySchema> LockAsync(LibraryScope scope, Guid id) =>
        await InScope(db.LibrarySchemas.FromSql($"SELECT * FROM library_schemas WHERE \"Id\" = {id} FOR UPDATE"), scope)
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Library schema not found");

    private async Task SaveHandlingSlugConflictAsync(string slug)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LibrarySchemaConfiguration.GlobalSlugIndex or LibrarySchemaConfiguration.AppSlugIndex
        })
        {
            throw new ConflictException($"A library schema with slug '{slug}' already exists");
        }
    }
}
