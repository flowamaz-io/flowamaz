using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Connector;

/// <summary>
/// Catalogue entry for a connector (official or community). Official connectors have
/// WorkspaceId = null; community connectors are scoped to a workspace.
/// </summary>
public class ConnectorDefinition : AuditableEntity
{
    /// <summary>Unique slug identifier, e.g. "http-rest", "postgresql".</summary>
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>Publisher slug, e.g. "flowamaz-io".</summary>
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>SemVer string, e.g. "1.0.0".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Category slug: generic, database, messaging, devops, productivity.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Searchable tags stored as jsonb string array.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Full connector manifest stored as jsonb.</summary>
    public string ManifestJson { get; set; } = "{}";

    /// <summary>Source URL for community connectors (GitHub repo, marketplace entry, etc.).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Tier classification controls install permissions.</summary>
    public ConnectorTier Tier { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Null for official connectors. Set for workspace-private community connectors.
    /// </summary>
    public Guid? WorkspaceId { get; set; }
}
