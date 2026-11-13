namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// A reusable human-gate form (FUNCTIONAL.md §23.2, Library Forms). Library forms (<see cref="WorkspaceId"/> null)
/// are seeded or synced from the templates repository's <c>forms/</c> folder; a workspace can save its own.
/// Using a form copies its fields into the gate's <c>config.form</c>, so later changes never alter a live gate.
/// </summary>
public class FormDefinition : AuditableEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Version { get; set; } = "1.0.0";
    public string? PublisherId { get; set; }
    public bool IsOfficial { get; set; }

    /// <summary>Null for Library forms; set for a form a workspace saved for itself.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>The fields as JSON (<c>GateFormField[]</c>).</summary>
    public string FieldsJson { get; set; } = "[]";

    public string[] Tags { get; set; } = [];
    public DateTime? DeprecatedAt { get; set; }
    public string? DeprecationNote { get; set; }
}

/// <summary>
/// A bundle for one domain (FUNCTIONAL.md §23.2, Library Packs): connectors, templates and forms installed
/// together. Seeded or synced from the templates repository's <c>packs/</c> folder.
/// </summary>
public class PackDefinition : AuditableEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Version { get; set; } = "1.0.0";
    public string? PublisherId { get; set; }
    public bool IsOfficial { get; set; }

    /// <summary>Connector references (id or publisher/id).</summary>
    public string[] Connectors { get; set; } = [];

    /// <summary>Template slugs; each is installed as a new workflow.</summary>
    public string[] Templates { get; set; } = [];

    /// <summary>Form slugs the pack recommends (forms are used from the gate editor, nothing to install).</summary>
    public string[] Forms { get; set; } = [];

    public string[] Tags { get; set; } = [];
    public DateTime? DeprecatedAt { get; set; }
    public string? DeprecationNote { get; set; }
}
