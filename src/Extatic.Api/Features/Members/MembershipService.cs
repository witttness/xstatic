using System.Text;
using System.Text.Json;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Members;

/// <summary>Admin actions on a membership (the state machine's edges).</summary>
public enum MembershipAction
{
    Approve,
    Reject,
    Suspend,
    Reinstate,
    Cancel,
    Expire,
    Renew
}

/// <summary>
/// Membership plans, memberships and settings. Every query is scoped by AppId. Capacity
/// (<see cref="MembershipPlan.MaxMembers"/>) is enforced under a row lock on the plan; member
/// numbers are allocated with an atomic UPDATE ... RETURNING on the app's settings row; the
/// one-open-membership-per-AppUser rule is a partial unique index.
/// </summary>
public class MembershipService(
    AppDbContext db,
    JsonSchemaValidator validator,
    WebhookDispatcher webhooks,
    IEmailQueue emailQueue,
    TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private static readonly Dictionary<MembershipAction, (MembershipStatus[] From, MembershipStatus To)> Transitions = new()
    {
        [MembershipAction.Approve] = ([MembershipStatus.Pending], MembershipStatus.Active),
        [MembershipAction.Reject] = ([MembershipStatus.Pending], MembershipStatus.Rejected),
        [MembershipAction.Suspend] = ([MembershipStatus.Active], MembershipStatus.Suspended),
        [MembershipAction.Reinstate] = ([MembershipStatus.Suspended], MembershipStatus.Active),
        [MembershipAction.Cancel] = (Membership.NonTerminal, MembershipStatus.Cancelled),
        [MembershipAction.Expire] = ([MembershipStatus.Active], MembershipStatus.Expired),
        [MembershipAction.Renew] = ([MembershipStatus.Active, MembershipStatus.Expired], MembershipStatus.Active),
    };

    public static bool CanTransition(MembershipStatus from, MembershipAction action)
        => Transitions[action].From.Contains(from);

    public static MembershipStatus TargetOf(MembershipAction action) => Transitions[action].To;

    // ------------------------------------------------------------------ access

    /// <summary>
    /// True when the AppUser has an Active, unexpired membership in the App (optionally of the
    /// plan with <paramref name="planSlug"/>). Public for other features.
    /// </summary>
    public async Task<bool> IsActiveMemberAsync(Guid appId, Guid appUserId, string? planSlug = null)
    {
        var now = Now;
        var query = db.Memberships.Where(m => m.AppId == appId && m.AppUserId == appUserId
            && m.Status == MembershipStatus.Active && (m.ExpiresAt == null || m.ExpiresAt > now));
        if (!string.IsNullOrEmpty(planSlug))
            query = query.Where(m => m.Plan.Slug == planSlug && m.Plan.AppId == appId);
        return await query.AnyAsync();
    }

    // ------------------------------------------------------------------ plans

    public async Task<List<MembershipPlanDto>> ListPlansAsync(Guid appId)
    {
        var plans = await db.MembershipPlans.AsNoTracking().Where(p => p.AppId == appId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();
        var counts = await db.Memberships.Where(m => m.AppId == appId)
            .GroupBy(m => new { m.PlanId, m.Status })
            .Select(g => new { g.Key.PlanId, g.Key.Status, Count = g.Count() })
            .ToListAsync();
        return plans.Select(p => p.ToDto(
            counts.Where(c => c.PlanId == p.Id && c.Status == MembershipStatus.Active).Sum(c => c.Count),
            counts.Where(c => c.PlanId == p.Id).Sum(c => c.Count))).ToList();
    }

    public async Task<MembershipPlanDto> GetPlanDtoAsync(Guid appId, Guid planId)
    {
        var plan = await FindPlanAsync(appId, planId);
        var active = await db.Memberships.CountAsync(m => m.AppId == appId && m.PlanId == planId && m.Status == MembershipStatus.Active);
        var total = await db.Memberships.CountAsync(m => m.AppId == appId && m.PlanId == planId);
        return plan.ToDto(active, total);
    }

    private async Task<MembershipPlan> FindPlanAsync(Guid appId, Guid planId)
        => await db.MembershipPlans.FirstOrDefaultAsync(p => p.AppId == appId && p.Id == planId)
           ?? throw new NotFoundException("Membership plan not found");

    public async Task<MembershipPlan> CreatePlanAsync(Guid appId, MembershipPlanRequest req)
    {
        var plan = new MembershipPlan { AppId = appId };
        ApplyPlan(plan, req);
        if (await db.MembershipPlans.AnyAsync(p => p.AppId == appId && p.Slug == plan.Slug))
            throw new ConflictException($"A plan with slug '{plan.Slug}' already exists");
        db.MembershipPlans.Add(plan);
        await SaveTranslatingUniqueAsync($"A plan with slug '{plan.Slug}' already exists");
        return plan;
    }

    public async Task<MembershipPlan> UpdatePlanAsync(Guid appId, Guid planId, MembershipPlanRequest req)
    {
        var plan = await FindPlanAsync(appId, planId);
        ApplyPlan(plan, req);
        if (await db.MembershipPlans.AnyAsync(p => p.AppId == appId && p.Slug == plan.Slug && p.Id != planId))
            throw new ConflictException($"A plan with slug '{plan.Slug}' already exists");
        await SaveTranslatingUniqueAsync($"A plan with slug '{plan.Slug}' already exists");
        return plan;
    }

    public async Task DeletePlanAsync(Guid appId, Guid planId)
    {
        var plan = await FindPlanAsync(appId, planId);
        if (await db.Memberships.AnyAsync(m => m.AppId == appId && m.PlanId == planId))
            throw new ConflictException("This plan has memberships and cannot be deleted. Deactivate it instead.");
        db.MembershipPlans.Remove(plan);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ConflictException("This plan has memberships and cannot be deleted. Deactivate it instead.");
        }
    }

    /// <summary>PUT semantics: nullable text/limit fields are cleared when null; booleans and sort order keep their value.</summary>
    private static void ApplyPlan(MembershipPlan plan, MembershipPlanRequest req)
    {
        var errors = new List<ValidationError>();
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MembersLimits.PlanNameMax)
            errors.Add(new ValidationError("name", $"Name is required (max {MembersLimits.PlanNameMax} characters)"));
        if (!Slugs.IsValid(req.Slug))
            errors.Add(new ValidationError("slug", "Must be 1-100 characters: lowercase letters, digits and hyphens"));
        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (description?.Length > MembersLimits.PlanDescriptionMax)
            errors.Add(new ValidationError("description", $"Max {MembersLimits.PlanDescriptionMax} characters"));
        var price = string.IsNullOrWhiteSpace(req.PriceLabel) ? null : req.PriceLabel.Trim();
        if (price?.Length > MembersLimits.PriceLabelMax)
            errors.Add(new ValidationError("price_label", $"Max {MembersLimits.PriceLabelMax} characters"));
        if (req.DurationDays is < 1 or > MembersLimits.MaxDurationDays)
            errors.Add(new ValidationError("duration_days", $"Must be between 1 and {MembersLimits.MaxDurationDays}, or null for no expiry"));
        if (req.MaxMembers is < 1)
            errors.Add(new ValidationError("max_members", "Must be at least 1, or null for unlimited"));
        var benefits = (req.Benefits ?? []).Select(b => b?.Trim() ?? "").Where(b => b.Length > 0).ToList();
        if (benefits.Count > MembersLimits.MaxBenefits)
            errors.Add(new ValidationError("benefits", $"At most {MembersLimits.MaxBenefits} benefits"));
        for (var i = 0; i < benefits.Count; i++)
            if (benefits[i].Length > MembersLimits.BenefitMax)
                errors.Add(new ValidationError($"benefits/{i}", $"Max {MembersLimits.BenefitMax} characters"));
        if (errors.Count > 0) throw new ValidationException("Invalid membership plan", errors);

        plan.Name = name!;
        plan.Slug = req.Slug!;
        plan.Description = description;
        plan.PriceLabel = price;
        plan.DurationDays = req.DurationDays;
        plan.MaxMembers = req.MaxMembers;
        plan.Benefits = benefits;
        plan.RequiresApproval = req.RequiresApproval ?? plan.RequiresApproval;
        plan.SelfSignupEnabled = req.SelfSignupEnabled ?? plan.SelfSignupEnabled;
        plan.IsActive = req.IsActive ?? plan.IsActive;
        plan.SortOrder = req.SortOrder ?? plan.SortOrder;
    }

    public async Task<List<ClientPlanDto>> ListClientPlansAsync(Guid appId)
    {
        var plans = await db.MembershipPlans.AsNoTracking().Where(p => p.AppId == appId && p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();
        var active = await db.Memberships
            .Where(m => m.AppId == appId && m.Status == MembershipStatus.Active)
            .GroupBy(m => m.PlanId).Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count);
        return plans.Select(p => p.ToClientDto(
            p.MaxMembers is { } max && active.GetValueOrDefault(p.Id) >= max)).ToList();
    }

    // ------------------------------------------------------------------ settings

    /// <summary>Settings for display/validation; an unsaved default instance when none exist yet.</summary>
    public async Task<MemberSettings> GetSettingsAsync(Guid appId)
        => await db.MemberSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AppId == appId)
           ?? new MemberSettings { AppId = appId };

    private async Task EnsureSettingsRowAsync(Guid appId)
    {
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO member_settings ("Id", "AppId", "DirectoryEnabled", "DirectoryFields", "MemberNumberPrefix",
                "NextMemberNumber", "NotifyEmails", "RenewalReminderDays", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {appId}, FALSE, {Array.Empty<string>()}, {MemberSettings.DefaultPrefix},
                1, {Array.Empty<string>()}, {MemberSettings.DefaultReminderDays}, {now}, {now})
            ON CONFLICT ("AppId") DO NOTHING
            """);
    }

    public async Task<MemberSettings> UpdateSettingsAsync(Guid appId, UpdateMemberSettingsRequest req)
    {
        var errors = new List<ValidationError>();
        string? schema = null;
        if (req.ProfileSchema is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } s)
        {
            schema = s.GetRawText();
            if (s.ValueKind != JsonValueKind.Object)
                errors.Add(new ValidationError("profile_schema", "Must be a JSON Schema object"));
            else if (Encoding.UTF8.GetByteCount(schema) > MembersLimits.MaxSchemaBytes)
                errors.Add(new ValidationError("profile_schema", "Schema is too large"));
            else
            {
                try
                {
                    var compiled = JsonSchema.FromText(schema, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
                    compiled.Evaluate(JsonDocument.Parse("{}").RootElement);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(new ValidationError("profile_schema", "Not a valid JSON Schema: " + ex.Message));
                }
            }
        }

        var fields = (req.DirectoryFields ?? []).Select(f => f?.Trim() ?? "").Where(f => f.Length > 0).Distinct().ToList();
        if (fields.Count > MembersLimits.MaxDirectoryFields)
            errors.Add(new ValidationError("directory_fields", $"At most {MembersLimits.MaxDirectoryFields} fields"));
        if (fields.Any(f => f.Length > 100))
            errors.Add(new ValidationError("directory_fields", "Field names are at most 100 characters"));

        var prefix = req.MemberNumberPrefix?.Trim() ?? MemberSettings.DefaultPrefix;
        if (prefix.Length > MembersLimits.PrefixMax || prefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')))
            errors.Add(new ValidationError("member_number_prefix",
                $"At most {MembersLimits.PrefixMax} characters: letters, digits, '-', '_', '.', '/'"));

        var notify = (req.NotifyEmails ?? []).Select(e => e?.Trim() ?? "").Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (notify.Count > MembersLimits.MaxNotifyEmails)
            errors.Add(new ValidationError("notify_emails", $"At most {MembersLimits.MaxNotifyEmails} addresses"));
        for (var i = 0; i < notify.Count; i++)
            if (!EmailQueue.IsValidAddress(notify[i]))
                errors.Add(new ValidationError($"notify_emails/{i}", "Must be a valid email address"));

        var days = (req.RenewalReminderDays ?? []).Distinct().OrderByDescending(d => d).ToList();
        if (days.Count > MembersLimits.MaxReminderOffsets)
            errors.Add(new ValidationError("renewal_reminder_days", $"At most {MembersLimits.MaxReminderOffsets} offsets"));
        if (days.Any(d => d < 1 || d > MembersLimits.MaxReminderDay))
            errors.Add(new ValidationError("renewal_reminder_days", $"Each offset must be between 1 and {MembersLimits.MaxReminderDay} days"));

        if (errors.Count > 0) throw new ValidationException("Invalid member settings", errors);

        await EnsureSettingsRowAsync(appId);
        var settings = await db.MemberSettings.FirstAsync(x => x.AppId == appId);
        settings.ProfileSchema = schema;
        settings.DirectoryEnabled = req.DirectoryEnabled ?? false;
        settings.DirectoryFields = [.. fields];
        settings.MemberNumberPrefix = prefix;
        settings.NotifyEmails = [.. notify];
        settings.RenewalReminderDays = [.. days];
        await db.SaveChangesAsync();
        return settings;
    }

    // ------------------------------------------------------------------ client: me

    /// <summary>The open (Pending/Active/Suspended) membership, else the most recent closed one, else null.</summary>
    public async Task<Membership?> GetLatestForUserAsync(Guid appId, Guid appUserId)
        => await db.Memberships.AsNoTracking().Include(m => m.Plan)
            .Where(m => m.AppId == appId && m.AppUserId == appUserId)
            .OrderBy(m => m.Status == MembershipStatus.Pending || m.Status == MembershipStatus.Active
                          || m.Status == MembershipStatus.Suspended ? 0 : 1)
            .ThenByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

    private async Task<Membership> GetOpenForUserTrackedAsync(Guid appId, Guid appUserId)
        => await db.Memberships.Include(m => m.Plan).Include(m => m.AppUser)
               .FirstOrDefaultAsync(m => m.AppId == appId && m.AppUserId == appUserId
                   && (m.Status == MembershipStatus.Pending || m.Status == MembershipStatus.Active
                       || m.Status == MembershipStatus.Suspended))
           ?? throw new NotFoundException("You do not have a current membership");

    public async Task<Membership> JoinAsync(Guid appId, Guid appUserId, JoinMembershipRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlanSlug))
            throw new ValidationException("Invalid request", [new ValidationError("plan_slug", "Plan is required")]);
        var plan = await db.MembershipPlans.FirstOrDefaultAsync(p => p.AppId == appId && p.Slug == req.PlanSlug && p.IsActive)
                   ?? throw new NotFoundException("Membership plan not found");
        if (!plan.SelfSignupEnabled)
            throw new ForbiddenException("This plan does not accept sign-ups");
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.AppId == appId && u.Id == appUserId)
                   ?? throw new NotFoundException("User not found");
        var settings = await GetSettingsAsync(appId);
        var profile = ValidateProfile(settings, req.Profile, required: false);

        var now = Now;
        var activate = !plan.RequiresApproval;
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            AppUserId = appUserId,
            PlanId = plan.Id,
            Plan = plan,
            AppUser = user,
            Status = activate ? MembershipStatus.Active : MembershipStatus.Pending,
            Profile = profile,
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        await LockPlanAsync(appId, plan.Id);
        if (await HasOpenMembershipAsync(appId, appUserId))
            throw new ConflictException("You already have a membership");
        if (activate)
        {
            await EnsureCapacityAsync(plan);
            membership.ApprovedAt = now;
            await ActivateTermAsync(membership, plan, now);
        }
        db.Memberships.Add(membership);
        AddEvent(membership, "applied", null, membership.Status, actorAppUserId: appUserId);

        var appName = await AppNameAsync(appId);
        var ctx = EmailContext(appName, plan, user, membership);
        await webhooks.StageAsync(db, appId, "member.applied", membership.ToWebhookData(plan, user));
        if (activate)
        {
            await webhooks.StageAsync(db, appId, "member.activated", membership.ToWebhookData(plan, user));
            StageEmail(appId, user.Email, to => MemberEmails.Welcome(to, ctx));
        }
        else
        {
            StageEmail(appId, user.Email, to => MemberEmails.ApplicationReceived(to, ctx));
        }
        foreach (var to in settings.NotifyEmails)
            StageEmail(appId, to, t => MemberEmails.NewApplicationNotice(t, ctx, user.Email, pending: !activate));

        await SaveTranslatingUniqueAsync("You already have a membership");
        await tx.CommitAsync();
        return membership;
    }

    public async Task<Membership> UpdateProfileAsync(Guid appId, Guid appUserId, JsonElement profile)
    {
        var membership = await GetOpenForUserTrackedAsync(appId, appUserId);
        var settings = await GetSettingsAsync(appId);
        membership.Profile = ValidateProfile(settings, profile, required: true);
        AddEvent(membership, "profile_updated", null, null, actorAppUserId: appUserId);
        await db.SaveChangesAsync();
        return membership;
    }

    public async Task<Membership> SetDirectoryOptInAsync(Guid appId, Guid appUserId, bool show)
    {
        var membership = await GetOpenForUserTrackedAsync(appId, appUserId);
        if (membership.ShowInDirectory != show)
        {
            membership.ShowInDirectory = show;
            AddEvent(membership, show ? "directory_opt_in" : "directory_opt_out", null, null, actorAppUserId: appUserId);
            await db.SaveChangesAsync();
        }
        return membership;
    }

    /// <summary>Member-initiated cancel: allowed from Pending or Active (not while Suspended).</summary>
    public async Task<Membership> MemberCancelAsync(Guid appId, Guid appUserId)
    {
        var open = await db.Memberships.Where(m => m.AppId == appId && m.AppUserId == appUserId
                && (m.Status == MembershipStatus.Pending || m.Status == MembershipStatus.Active
                    || m.Status == MembershipStatus.Suspended))
            .Select(m => new { m.Id, m.Status }).FirstOrDefaultAsync()
            ?? throw new NotFoundException("You do not have a current membership");
        if (open.Status == MembershipStatus.Suspended)
            throw new ConflictException("A suspended membership cannot be cancelled by the member");
        return await TransitionAsync(appId, open.Id, MembershipAction.Cancel, actorUserId: null, reason: null,
            actorAppUserId: appUserId, allowedFrom: [MembershipStatus.Pending, MembershipStatus.Active]);
    }

    public async Task<PagedResult<DirectoryEntryDto>> GetDirectoryAsync(Guid appId, Guid appUserId, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var settings = await GetSettingsAsync(appId);
        if (!settings.DirectoryEnabled) throw new NotFoundException("The member directory is not enabled");
        if (!await IsActiveMemberAsync(appId, appUserId))
            throw new ForbiddenException("Only active members can view the directory");

        var now = Now;
        var query = db.Memberships.AsNoTracking().Where(m => m.AppId == appId && m.ShowInDirectory
            && m.Status == MembershipStatus.Active && (m.ExpiresAt == null || m.ExpiresAt > now));
        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(m => m.AppUser.DisplayName).ThenBy(m => m.MemberNumber).ThenBy(m => m.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize)
            .Select(m => new { m.AppUser.DisplayName, m.Profile })
            .ToListAsync();
        var allowed = new HashSet<string>(settings.DirectoryFields, StringComparer.Ordinal);
        var items = rows.Select(r => new DirectoryEntryDto(r.DisplayName, FilterFields(r.Profile, allowed))).ToList();
        return new PagedResult<DirectoryEntryDto>(items, total, page, pageSize);
    }

    private static Dictionary<string, JsonElement> FilterFields(string profile, HashSet<string> allowed)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (allowed.Count == 0) return result;
        using var doc = JsonDocument.Parse(profile);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (allowed.Contains(prop.Name))
                result[prop.Name] = prop.Value.Clone();
        return result;
    }

    private string ValidateProfile(MemberSettings settings, JsonElement? profile, bool required)
    {
        if (profile is null || profile.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (required)
                throw new ValidationException("Invalid profile", [new ValidationError("profile", "Profile is required")]);
            profile = JsonDocument.Parse("{}").RootElement;
        }
        var value = profile.Value;
        if (value.ValueKind != JsonValueKind.Object)
            throw new ValidationException("Invalid profile", [new ValidationError("profile", "Profile must be a JSON object")]);
        var raw = value.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > MembersLimits.MaxProfileBytes)
            throw new ValidationException("Invalid profile", [new ValidationError("profile", "Profile is too large")]);
        if (settings.ProfileSchema is not null)
        {
            var errors = validator.Validate(settings.ProfileSchema, value);
            if (errors.Count > 0)
                throw new ValidationException("Profile does not match the member profile schema",
                    errors.Select(e => new ValidationError("profile" + e.Pointer, e.Message)));
        }
        return raw;
    }

    // ------------------------------------------------------------------ platform: memberships

    public async Task<PagedResult<MembershipSummaryDto>> ListAsync(Guid appId, string? status, Guid? planId,
        string? search, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = Filter(appId, status, planId, search);
        var total = await query.CountAsync();
        var rows = await query.Include(m => m.AppUser).Include(m => m.Plan)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<MembershipSummaryDto>(rows.Select(m => m.ToSummaryDto()).ToList(), total, page, pageSize);
    }

    private IQueryable<Membership> Filter(Guid appId, string? status, Guid? planId, string? search)
    {
        var query = db.Memberships.AsNoTracking().Where(m => m.AppId == appId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MembershipStatus>(status, ignoreCase: true, out var st) || int.TryParse(status, out _))
                throw new BadRequestException("Unknown status filter");
            query = query.Where(m => m.Status == st);
        }
        if (planId is { } pid) query = query.Where(m => m.PlanId == pid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 200) term = term[..200];
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(m =>
                EF.Functions.ILike(m.AppUser.DisplayName ?? "", pattern, "\\")
                || EF.Functions.ILike(m.AppUser.Email ?? "", pattern, "\\")
                || EF.Functions.ILike(m.MemberNumber ?? "", pattern, "\\"));
        }
        return query;
    }

    public async Task<MembershipDetailDto> GetDetailAsync(Guid appId, Guid membershipId)
    {
        var m = await db.Memberships.AsNoTracking().Include(x => x.AppUser).Include(x => x.Plan)
                    .FirstOrDefaultAsync(x => x.AppId == appId && x.Id == membershipId)
                ?? throw new NotFoundException("Membership not found");
        var events = await db.MembershipEvents.AsNoTracking()
            .Where(e => e.AppId == appId && e.MembershipId == membershipId).ToListAsync();
        return m.ToDetailDto(events);
    }

    public async Task<Membership> CreateAsync(Guid appId, Guid actorUserId, CreateMembershipRequest req)
    {
        MembershipStatus status;
        if (string.IsNullOrWhiteSpace(req.Status) || req.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            status = MembershipStatus.Active;
        else if (req.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
            status = MembershipStatus.Pending;
        else
            throw new ValidationException("Invalid request", [new ValidationError("status", "Must be 'active' or 'pending'")]);
        if (req.Notes?.Length > MembersLimits.NotesMax)
            throw new ValidationException("Invalid request", [new ValidationError("notes", $"Max {MembersLimits.NotesMax} characters")]);

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.AppId == appId && u.Id == req.AppUserId)
                   ?? throw new NotFoundException("User not found");
        var plan = await FindPlanAsync(appId, req.PlanId);
        var settings = await GetSettingsAsync(appId);
        // An admin may omit the profile even when the schema requires fields.
        var profile = req.Profile is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }
            ? ValidateProfile(settings, req.Profile, required: true)
            : "{}";

        var now = Now;
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            AppUserId = user.Id,
            PlanId = plan.Id,
            Plan = plan,
            AppUser = user,
            Status = status,
            Profile = profile,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes,
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        await LockPlanAsync(appId, plan.Id);
        if (await HasOpenMembershipAsync(appId, user.Id))
            throw new ConflictException("This user already has a membership");
        if (status == MembershipStatus.Active)
        {
            await EnsureCapacityAsync(plan);
            membership.ApprovedAt = now;
            membership.ApprovedByUserId = actorUserId;
            await ActivateTermAsync(membership, plan, now);
        }
        db.Memberships.Add(membership);
        AddEvent(membership, "created", null, status, actorUserId: actorUserId);

        if (status == MembershipStatus.Active)
        {
            var ctx = EmailContext(await AppNameAsync(appId), plan, user, membership);
            await webhooks.StageAsync(db, appId, "member.activated", membership.ToWebhookData(plan, user));
            StageEmail(appId, user.Email, to => MemberEmails.Welcome(to, ctx));
        }
        else
        {
            await webhooks.StageAsync(db, appId, "member.applied", membership.ToWebhookData(plan, user));
        }

        await SaveTranslatingUniqueAsync("This user already has a membership");
        await tx.CommitAsync();
        return membership;
    }

    public Task<Membership> TransitionAsync(Guid appId, Guid membershipId, MembershipAction action,
        Guid actorUserId, string? reason)
        => TransitionAsync(appId, membershipId, action, actorUserId, reason, actorAppUserId: null, allowedFrom: null);

    private async Task<Membership> TransitionAsync(Guid appId, Guid membershipId, MembershipAction action,
        Guid? actorUserId, string? reason, Guid? actorAppUserId, MembershipStatus[]? allowedFrom)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > MembersLimits.ReasonMax)
            throw new ValidationException("Invalid request", [new ValidationError("reason", $"Max {MembersLimits.ReasonMax} characters")]);

        await using var tx = await db.Database.BeginTransactionAsync();
        await LockMembershipAsync(appId, membershipId);
        var m = await db.Memberships.Include(x => x.Plan).Include(x => x.AppUser)
                    .FirstOrDefaultAsync(x => x.AppId == appId && x.Id == membershipId)
                ?? throw new NotFoundException("Membership not found");
        await db.Entry(m).ReloadAsync(); // the row may have changed between our snapshot and the lock

        var from = m.Status;
        if (!CanTransition(from, action) || (allowedFrom is not null && !allowedFrom.Contains(from)))
            throw new ConflictException($"Cannot {action.ToString().ToLowerInvariant()} a membership that is {from.ToApi()}");

        var to = TargetOf(action);
        var now = Now;
        var plan = m.Plan;
        string? webhookEvent = null;
        Func<string, MemberEmails.Context, EmailMessage>? email = null;

        if (to == MembershipStatus.Active && from != MembershipStatus.Active)
        {
            await LockPlanAsync(appId, plan.Id);
            await EnsureCapacityAsync(plan);
        }

        switch (action)
        {
            case MembershipAction.Approve:
                m.ApprovedAt = now;
                m.ApprovedByUserId = actorUserId;
                await ActivateTermAsync(m, plan, now);
                webhookEvent = "member.activated";
                email = MemberEmails.Welcome;
                break;
            case MembershipAction.Reject:
                email = MemberEmails.Rejected;
                break;
            case MembershipAction.Suspend:
                webhookEvent = "member.suspended";
                email = MemberEmails.Suspended;
                break;
            case MembershipAction.Reinstate:
                webhookEvent = "member.reinstated";
                email = MemberEmails.Reinstated;
                break;
            case MembershipAction.Cancel:
                m.CancelledAt = now;
                webhookEvent = "member.cancelled";
                email = MemberEmails.Cancelled;
                break;
            case MembershipAction.Expire:
                webhookEvent = "member.expired";
                email = MemberEmails.Expired;
                break;
            case MembershipAction.Renew:
                if (plan.DurationDays is not { } days)
                    throw new ConflictException("This plan does not expire, so there is nothing to renew");
                m.ExpiresAt = RenewedExpiry(m.ExpiresAt, days, now);
                m.RemindersSent = [];
                m.MemberNumber ??= await AllocateMemberNumberAsync(appId);
                webhookEvent = "member.renewed";
                break;
        }

        m.Status = to;
        AddEvent(m, action.ToString().ToLowerInvariant(), from, to, actorUserId, actorAppUserId, reason);
        if (webhookEvent is not null)
            await webhooks.StageAsync(db, appId, webhookEvent, m.ToWebhookData(plan, m.AppUser));
        if (email is not null)
        {
            var ctx = EmailContext(await AppNameAsync(appId), plan, m.AppUser, m) with { Reason = reason };
            StageEmail(appId, m.AppUser.Email, address => email(address, ctx));
        }

        await SaveTranslatingUniqueAsync("This user already has another open membership");
        await tx.CommitAsync();
        return m;
    }

    /// <summary>Renewal extends from the later of now and the current expiry.</summary>
    public static DateTime RenewedExpiry(DateTime? currentExpiry, int durationDays, DateTime now)
    {
        var start = currentExpiry is { } e && e > now ? e : now;
        return start.AddDays(durationDays);
    }

    public async Task<Membership> ChangePlanAsync(Guid appId, Guid membershipId, Guid planId, Guid actorUserId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockMembershipAsync(appId, membershipId);
        var m = await db.Memberships.Include(x => x.Plan).Include(x => x.AppUser)
                    .FirstOrDefaultAsync(x => x.AppId == appId && x.Id == membershipId)
                ?? throw new NotFoundException("Membership not found");
        await db.Entry(m).ReloadAsync();
        if (!Membership.NonTerminal.Contains(m.Status))
            throw new ConflictException($"Cannot change the plan of a membership that is {m.Status.ToApi()}");
        var newPlan = await FindPlanAsync(appId, planId);
        if (newPlan.Id == m.PlanId) return m;
        if (m.Status == MembershipStatus.Active)
        {
            await LockPlanAsync(appId, newPlan.Id);
            await EnsureCapacityAsync(newPlan);
        }
        var oldSlug = m.Plan.Slug;
        m.PlanId = newPlan.Id;
        m.Plan = newPlan;
        AddEvent(m, "plan_changed", m.Status, m.Status, actorUserId: actorUserId, detail: $"{oldSlug} -> {newPlan.Slug}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return m;
    }

    public async Task<Membership> UpdateNotesAsync(Guid appId, Guid membershipId, string? notes, Guid actorUserId)
    {
        if (notes?.Length > MembersLimits.NotesMax)
            throw new ValidationException("Invalid request", [new ValidationError("notes", $"Max {MembersLimits.NotesMax} characters")]);
        var m = await db.Memberships.Include(x => x.Plan).Include(x => x.AppUser)
                    .FirstOrDefaultAsync(x => x.AppId == appId && x.Id == membershipId)
                ?? throw new NotFoundException("Membership not found");
        m.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        AddEvent(m, "notes_updated", null, null, actorUserId: actorUserId);
        await db.SaveChangesAsync();
        return m;
    }

    public async Task<byte[]> ExportCsvAsync(Guid appId, string? status, Guid? planId, string? search)
    {
        var rows = await Filter(appId, status, planId, search).Include(m => m.AppUser).Include(m => m.Plan)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync();
        var csv = new CsvBuilder().AddRow("member_number", "display_name", "email", "plan", "status", "starts_at",
            "expires_at", "approved_at", "cancelled_at", "show_in_directory", "created_at", "profile");
        foreach (var m in rows)
            csv.AddRow(m.MemberNumber, m.AppUser.DisplayName, m.AppUser.Email, m.Plan.Slug, m.Status.ToApi(), m.StartsAt,
                m.ExpiresAt, m.ApprovedAt, m.CancelledAt, m.ShowInDirectory, m.CreatedAt, m.Profile);
        return csv.ToBytes();
    }

    public async Task<MembersStatsDto> GetStatsAsync(Guid appId)
    {
        var now = Now;
        var groups = await db.Memberships.Where(m => m.AppId == appId)
            .GroupBy(m => new { m.PlanId, m.Status })
            .Select(g => new { g.Key.PlanId, g.Key.Status, Count = g.Count() }).ToListAsync();
        var plans = await db.MembershipPlans.AsNoTracking().Where(p => p.AppId == appId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();
        var limit = now.AddDays(30);
        var expiring = await db.Memberships.CountAsync(m => m.AppId == appId && m.Status == MembershipStatus.Active
            && m.ExpiresAt != null && m.ExpiresAt > now && m.ExpiresAt <= limit);

        Dictionary<string, int> ByStatus(IEnumerable<(MembershipStatus Status, int Count)> src)
        {
            var d = Enum.GetValues<MembershipStatus>().ToDictionary(s => s.ToApi(), _ => 0);
            foreach (var (s, c) in src) d[s.ToApi()] += c;
            return d;
        }

        return new MembersStatsDto(
            groups.Sum(g => g.Count),
            ByStatus(groups.Select(g => (g.Status, g.Count))),
            plans.Select(p => new PlanStatsDto(p.Id, p.Slug, p.Name,
                ByStatus(groups.Where(g => g.PlanId == p.Id).Select(g => (g.Status, g.Count))),
                groups.Where(g => g.PlanId == p.Id).Sum(g => g.Count))).ToList(),
            expiring);
    }

    // ------------------------------------------------------------------ helpers

    private Task<bool> HasOpenMembershipAsync(Guid appId, Guid appUserId)
        => db.Memberships.AnyAsync(m => m.AppId == appId && m.AppUserId == appUserId
            && (m.Status == MembershipStatus.Pending || m.Status == MembershipStatus.Active
                || m.Status == MembershipStatus.Suspended));

    /// <summary>Row lock on the plan for the rest of the transaction (serialises capacity checks).</summary>
    private async Task LockPlanAsync(Guid appId, Guid planId)
    {
        var ids = await db.Database.SqlQuery<Guid>(
            $"""SELECT "Id" AS "Value" FROM membership_plans WHERE "Id" = {planId} AND "AppId" = {appId} FOR UPDATE""")
            .ToListAsync();
        if (ids.Count == 0) throw new NotFoundException("Membership plan not found");
    }

    private async Task LockMembershipAsync(Guid appId, Guid membershipId)
    {
        var ids = await db.Database.SqlQuery<Guid>(
            $"""SELECT "Id" AS "Value" FROM memberships WHERE "Id" = {membershipId} AND "AppId" = {appId} FOR UPDATE""")
            .ToListAsync();
        if (ids.Count == 0) throw new NotFoundException("Membership not found");
    }

    /// <summary>Call with the plan row locked. Counts Active memberships committed so far.</summary>
    private async Task EnsureCapacityAsync(MembershipPlan plan)
    {
        // Re-read the cap under the lock: the tracked plan may predate a concurrent edit.
        var fresh = await db.MembershipPlans.Where(p => p.Id == plan.Id).Select(p => p.MaxMembers).FirstAsync();
        if (fresh is not { } max) return;
        var active = await db.Memberships.CountAsync(m => m.AppId == plan.AppId && m.PlanId == plan.Id
            && m.Status == MembershipStatus.Active);
        if (active >= max) throw new ConflictException("This plan is full");
    }

    /// <summary>Starts a new term now and assigns a member number on first activation.</summary>
    private async Task ActivateTermAsync(Membership m, MembershipPlan plan, DateTime now)
    {
        m.StartsAt = now;
        m.ExpiresAt = plan.DurationDays is { } days ? now.AddDays(days) : null;
        m.RemindersSent = [];
        m.MemberNumber ??= await AllocateMemberNumberAsync(m.AppId);
    }

    private sealed class AllocatedNumber
    {
        public long Number { get; set; }
        public string Prefix { get; set; } = string.Empty;
    }

    /// <summary>
    /// Atomic increment of the app's counter. The UPDATE row-locks the settings row until the
    /// caller's transaction ends, so concurrent allocations serialise and a rolled-back
    /// transaction does not consume a number.
    /// </summary>
    private async Task<string> AllocateMemberNumberAsync(Guid appId)
    {
        await EnsureSettingsRowAsync(appId);
        var rows = await db.Database.SqlQuery<AllocatedNumber>($"""
            UPDATE member_settings SET "NextMemberNumber" = "NextMemberNumber" + 1
            WHERE "AppId" = {appId}
            RETURNING "NextMemberNumber" - 1 AS "Number", "MemberNumberPrefix" AS "Prefix"
            """).ToListAsync();
        var row = rows.Single();
        return FormatMemberNumber(row.Prefix, row.Number);
    }

    public static string FormatMemberNumber(string prefix, long number) => $"{prefix}{number:D5}";

    private void AddEvent(Membership m, string type, MembershipStatus? from, MembershipStatus? to,
        Guid? actorUserId = null, Guid? actorAppUserId = null, string? detail = null)
        => db.MembershipEvents.Add(new MembershipEvent
        {
            AppId = m.AppId,
            MembershipId = m.Id,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = actorUserId,
            ActorAppUserId = actorAppUserId,
            Detail = detail,
        });

    private Task<string> AppNameAsync(Guid appId)
        => db.Apps.Where(a => a.Id == appId).Select(a => a.Name).FirstAsync();

    internal static MemberEmails.Context EmailContext(string appName, MembershipPlan plan, AppUser? user, Membership m)
        => new(appName, plan.Name, user?.DisplayName, m.MemberNumber, m.ExpiresAt, null);

    /// <summary>Stages an email when the address is valid (AppUsers may have no / an odd email).</summary>
    private void StageEmail(Guid appId, string? to, Func<string, EmailMessage> build)
    {
        if (!EmailQueue.IsValidAddress(to)) return;
        emailQueue.Stage(db, appId, build(to!.Trim()));
    }

    private async Task SaveTranslatingUniqueAsync(string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(message);
        }
    }
}
