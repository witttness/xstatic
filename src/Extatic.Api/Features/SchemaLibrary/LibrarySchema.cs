using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>Global = curated by platform admins; App = private to one App, curated by its owner/admins.</summary>
public enum LibrarySchemaScope
{
    Global,
    App
}

/// <summary>Only meaningful for Global schemas (App schemas store null).</summary>
public enum LibrarySchemaVisibility
{
    /// <summary>Visible and selectable only by platform admins (drafts, internal schemas).</summary>
    AdminOnly,

    /// <summary>Selectable by every tenant.</summary>
    Shared
}

/// <summary>
/// A reusable JSON Schema. Deliberately NOT <c>IAppScoped</c>: Global rows have no AppId (they are
/// platform-scoped), App rows carry their AppId and every App-scope query filters by it. A DB check
/// constraint enforces Scope/AppId/Visibility consistency.
/// </summary>
public class LibrarySchema
{
    public Guid Id { get; set; }
    public LibrarySchemaScope Scope { get; set; }
    public Guid? AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string[] Tags { get; set; } = [];
    public LibrarySchemaVisibility? Visibility { get; set; }

    /// <summary>The current version's content (jsonb).</summary>
    public string Schema { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public App? App { get; set; }
    public ICollection<LibrarySchemaVersion> Versions { get; set; } = [];
}

/// <summary>Immutable snapshot written whenever a library schema's content changes.</summary>
public class LibrarySchemaVersion
{
    public Guid Id { get; set; }
    public Guid LibrarySchemaId { get; set; }

    /// <summary>Mirrors the parent's AppId (null for Global schemas).</summary>
    public Guid? AppId { get; set; }
    public int Version { get; set; }
    public string Schema { get; set; } = "{}";
    public string? ChangeNote { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public LibrarySchema LibrarySchema { get; set; } = null!;
}
