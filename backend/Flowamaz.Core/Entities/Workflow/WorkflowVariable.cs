namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// A runtime variable scoped to a single instance. Sensitive values are encrypted at rest
/// (<see cref="IsSensitive"/>) and never returned in plain text or logged.
/// </summary>
public class WorkflowVariable : WorkspaceEntity
{
    public Guid InstanceId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>jsonb value. When <see cref="IsSensitive"/> is true this is ciphertext.</summary>
    public string Value { get; set; } = "null";
    public bool IsSensitive { get; set; }
}
