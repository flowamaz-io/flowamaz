namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// An immutable YAML snapshot of a workflow at a Git commit. One production version may exist
/// at a time per definition (<see cref="IsProduction"/>). Pinned by instances at trigger time.
/// </summary>
public class WorkflowVersion : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public string? TagName { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string YamlContent { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsProduction { get; set; }
}
