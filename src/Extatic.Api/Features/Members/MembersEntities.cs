using Extatic.Api.Domain;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.Members;

/// <summary>Stored as a string. Pending/Active/Suspended are non-terminal (at most one per AppUser).</summary>
public enum MembershipStatus
{
    Pending,
    Active,
    Suspended,
    Expired,
    Cancelled,
    Rejected
}

/// <summary>A tier members can join. Payments are out of scope; <see cref="PriceLabel"/> is display text.</summary>
public class MembershipPlan : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PriceLabel { get; set; }
    /// <summary>Length of one term; null = never expires.</summary>
    public int? DurationDays { get; set; }
    public bool RequiresApproval { get; set; }
    public bool SelfSignupEnabled { get; set; } = true;
    /// <summary>Cap on Active memberships of this plan; null = unlimited.</summary>
    public int? MaxMembers { get; set; }
    public List<string> Benefits { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}

public class Membership : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid AppUserId { get; set; }
    public Guid PlanId { get; set; }
    public MembershipStatus Status { get; set; }
    /// <summary>Per-app sequential, formatted with the app's prefix; assigned on first activation.</summary>
    public string? MemberNumber { get; set; }
    /// <summary>JSON object (jsonb), validated against <see cref="MemberSettings.ProfileSchema"/>.</summary>
    public string Profile { get; set; } = "{}";
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }
    /// <summary>Admin-only. Never returned by the Client API.</summary>
    public string? Notes { get; set; }
    public bool ShowInDirectory { get; set; }
    /// <summary>Reminder day offsets already sent for the current term.</summary>
    public int[] RemindersSent { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
    public AppUser AppUser { get; set; } = null!;
    public MembershipPlan Plan { get; set; } = null!;
    public ICollection<MembershipEvent> Events { get; set; } = [];

    public static readonly MembershipStatus[] NonTerminal =
        [MembershipStatus.Pending, MembershipStatus.Active, MembershipStatus.Suspended];
}

/// <summary>One per App, created lazily.</summary>
public class MemberSettings : IAppScoped
{
    public const string DefaultPrefix = "M-";
    public static readonly int[] DefaultReminderDays = [30, 7];

    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string? ProfileSchema { get; set; }
    public bool DirectoryEnabled { get; set; }
    public string[] DirectoryFields { get; set; } = [];
    public string MemberNumberPrefix { get; set; } = DefaultPrefix;
    public long NextMemberNumber { get; set; } = 1;
    public string[] NotifyEmails { get; set; } = [];
    public int[] RenewalReminderDays { get; set; } = [.. DefaultReminderDays];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App App { get; set; } = null!;
}

/// <summary>Audit trail entry for a membership.</summary>
public class MembershipEvent : IAppScoped
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid MembershipId { get; set; }
    public string Type { get; set; } = string.Empty;
    public MembershipStatus? FromStatus { get; set; }
    public MembershipStatus? ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorAppUserId { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }

    public App App { get; set; } = null!;
    public Membership Membership { get; set; } = null!;
}
