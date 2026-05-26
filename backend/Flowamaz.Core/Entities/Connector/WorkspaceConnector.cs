namespace Flowamaz.Core.Entities.Connector;

/// <summary>
/// Join record tracking which connector definitions are installed in a workspace.
/// One row per (WorkspaceId, ConnectorDefinitionId) pair.
/// </summary>
public class WorkspaceConnector : WorkspaceEntity
{
    public Guid ConnectorDefinitionId { get; set; }

    /// <summary>Active credential used by this connector installation. May be null if no auth needed.</summary>
    public Guid? CredentialId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    public Guid InstalledBy { get; set; }

    // Navigation
    public virtual ConnectorDefinition? ConnectorDefinition { get; set; }
    public virtual WorkspaceCredential? Credential { get; set; }
}
