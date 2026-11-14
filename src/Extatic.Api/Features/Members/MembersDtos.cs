using System.Text.Json;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Members;

public static class MembersLimits
{
    public const int PlanNameMax = 200;
    public const int PlanDescriptionMax = 4000;
    public const int PriceLabelMax = 100;
    public const int MaxBenefits = 50;
    public const int BenefitMax = 500;
    public const int MaxDurationDays = 36500;
    public const int NotesMax = 10000;
    public const int ReasonMax = 1000;
    public const int PrefixMax = 20;
    public const int MaxDirectoryFields = 50;
    public const int MaxNotifyEmails = 20;
    public const int MaxReminderOffsets = 10;
    public const int MaxReminderDay = 365;
    public const int MaxProfileBytes = 64 * 1024;
    public const int MaxSchemaBytes = 64 * 1024;
}

// ---- Platform API ----

public record MembershipPlanDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? PriceLabel,
    int? DurationDays,
    bool RequiresApproval,
    bool SelfSignupEnabled,
    int? MaxMembers,
    IReadOnlyList<string> Benefits,
    bool IsActive,
    int SortOrder,
    int ActiveMembers,
    int TotalMemberships,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Create (all required fields) or full replace of a plan.</summary>
public record MembershipPlanRequest(
    string? Name,
    string? Slug,
    string? Description,
    string? PriceLabel,
    int? DurationDays,
    bool? RequiresApproval,
    bool? SelfSignupEnabled,
    int? MaxMembers,
    List<string>? Benefits,
    bool? IsActive,
    int? SortOrder);

public record MembershipSummaryDto(
    Guid Id,
    Guid AppUserId,
    string? DisplayName,
    string? Email,
    Guid PlanId,
    string PlanSlug,
    string PlanName,
    string Status,
    string? MemberNumber,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    DateTime? ApprovedAt,
    DateTime? CancelledAt,
    bool ShowInDirectory,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record MembershipEventDto(
    Guid Id,
    string Type,
    string? FromStatus,
    string? ToStatus,
    Guid? ActorUserId,
    Guid? ActorAppUserId,
    string? Detail,
    DateTime CreatedAt);

public record MembershipDetailDto(
    MembershipSummaryDto Membership,
    JsonElement Profile,
    string? Notes,
    Guid? ApprovedByUserId,
    IReadOnlyList<int> RemindersSent,
    IReadOnlyList<MembershipEventDto> Events);

public record CreateMembershipRequest(
    Guid AppUserId,
    Guid PlanId,
    string? Status,
    JsonElement? Profile,
    string? Notes);

public record MembershipTransitionRequest(string? Reason);
public record ChangePlanRequest(Guid PlanId);
public record MembershipNotesRequest(string? Notes);

public record MemberSettingsDto(
    JsonElement? ProfileSchema,
    bool DirectoryEnabled,
    IReadOnlyList<string> DirectoryFields,
    string MemberNumberPrefix,
    long NextMemberNumber,
    IReadOnlyList<string> NotifyEmails,
    IReadOnlyList<int> RenewalReminderDays);

public record UpdateMemberSettingsRequest(
    JsonElement? ProfileSchema,
    bool? DirectoryEnabled,
    List<string>? DirectoryFields,
    string? MemberNumberPrefix,
    List<string>? NotifyEmails,
    List<int>? RenewalReminderDays);

public record PlanStatsDto(Guid PlanId, string Slug, string Name, IReadOnlyDictionary<string, int> ByStatus, int Total);

public record MembersStatsDto(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyList<PlanStatsDto> ByPlan,
    int ExpiringSoon);

// ---- Client API ----

public record ClientPlanDto(
    string Slug,
    string Name,
    string? Description,
    string? PriceLabel,
    int? DurationDays,
    bool RequiresApproval,
    bool SelfSignupEnabled,
    IReadOnlyList<string> Benefits,
    bool IsFull);

public record ClientPlanSummaryDto(string Slug, string Name, string? PriceLabel, int? DurationDays);

/// <summary>What a member sees about their own membership. Never includes Notes or admin ids.</summary>
public record ClientMembershipDto(
    string Status,
    ClientPlanSummaryDto Plan,
    string? MemberNumber,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    JsonElement Profile,
    bool ShowInDirectory,
    DateTime CreatedAt);

public record ClientMeResponse(ClientMembershipDto? Membership);
public record ClientAccessResponse(bool Active);
public record JoinMembershipRequest(string? PlanSlug, JsonElement? Profile);
public record UpdateProfileRequest(JsonElement Profile);
public record UpdateDirectoryRequest(bool ShowInDirectory);
public record DirectoryEntryDto(string? DisplayName, IReadOnlyDictionary<string, JsonElement> Fields);

public static class MembersMappings
{
    public static string ToApi(this MembershipStatus status) => status.ToString().ToLowerInvariant();

    public static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static MembershipPlanDto ToDto(this MembershipPlan p, int activeMembers = 0, int totalMemberships = 0) => new(
        p.Id, p.Name, p.Slug, p.Description, p.PriceLabel, p.DurationDays, p.RequiresApproval,
        p.SelfSignupEnabled, p.MaxMembers, p.Benefits, p.IsActive, p.SortOrder, activeMembers,
        totalMemberships, p.CreatedAt, p.UpdatedAt);

    public static ClientPlanDto ToClientDto(this MembershipPlan p, bool isFull) => new(
        p.Slug, p.Name, p.Description, p.PriceLabel, p.DurationDays, p.RequiresApproval,
        p.SelfSignupEnabled, p.Benefits, isFull);

    /// <summary>Requires <see cref="Membership.AppUser"/> and <see cref="Membership.Plan"/> loaded.</summary>
    public static MembershipSummaryDto ToSummaryDto(this Membership m) => new(
        m.Id, m.AppUserId, m.AppUser?.DisplayName, m.AppUser?.Email, m.PlanId, m.Plan.Slug, m.Plan.Name,
        m.Status.ToApi(), m.MemberNumber, m.StartsAt, m.ExpiresAt, m.ApprovedAt, m.CancelledAt,
        m.ShowInDirectory, m.CreatedAt, m.UpdatedAt);

    public static MembershipEventDto ToDto(this MembershipEvent e) => new(
        e.Id, e.Type, e.FromStatus?.ToApi(), e.ToStatus?.ToApi(), e.ActorUserId, e.ActorAppUserId,
        e.Detail, e.CreatedAt);

    public static MembershipDetailDto ToDetailDto(this Membership m, IEnumerable<MembershipEvent> events) => new(
        m.ToSummaryDto(), ParseJson(m.Profile), m.Notes, m.ApprovedByUserId, m.RemindersSent,
        events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => e.ToDto()).ToList());

    /// <summary>Requires <see cref="Membership.Plan"/> loaded.</summary>
    public static ClientMembershipDto ToClientDto(this Membership m) => new(
        m.Status.ToApi(),
        new ClientPlanSummaryDto(m.Plan.Slug, m.Plan.Name, m.Plan.PriceLabel, m.Plan.DurationDays),
        m.MemberNumber, m.StartsAt, m.ExpiresAt, ParseJson(m.Profile), m.ShowInDirectory, m.CreatedAt);

    public static MemberSettingsDto ToDto(this MemberSettings s) => new(
        s.ProfileSchema is null ? null : ParseJson(s.ProfileSchema), s.DirectoryEnabled, s.DirectoryFields,
        s.MemberNumberPrefix, s.NextMemberNumber, s.NotifyEmails, s.RenewalReminderDays);

    /// <summary>Webhook payload: no profile, no notes.</summary>
    public static JsonElement ToWebhookData(this Membership m, MembershipPlan plan, AppUser? user)
        => JsonSerializer.SerializeToElement(new
        {
            id = m.Id,
            app_user_id = m.AppUserId,
            email = user?.Email,
            display_name = user?.DisplayName,
            plan_slug = plan.Slug,
            status = m.Status.ToApi(),
            member_number = m.MemberNumber,
            starts_at = m.StartsAt,
            expires_at = m.ExpiresAt
        });
}
