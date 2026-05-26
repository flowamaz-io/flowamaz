using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// A workflow authored in a workspace. The YAML is the source of truth; <see cref="CurrentVersion"/>
/// points at the Git commit (or "draft"). Workspace-scoped and soft-deletable.
/// </summary>
public class WorkflowDefinition : WorkspaceEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string YamlContent { get; set; } = string.Empty;
    public string? NlDescription { get; set; }
    public WorkflowCreatedByMethod CreatedByMethod { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;
    public string CurrentVersion { get; set; } = "draft";
    public int HealthScore { get; set; } = 100;
    public WorkflowTriggerType TriggerType { get; set; } = WorkflowTriggerType.Manual;

    /// <summary>Per-workflow SLA target in milliseconds. Null = no SLA; drives SLA-risk insights.</summary>
    public long? SlaThresholdMs { get; set; }
}
