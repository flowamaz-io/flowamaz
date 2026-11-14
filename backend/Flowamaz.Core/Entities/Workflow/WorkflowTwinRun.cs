namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// One Workflow Twin simulation of a workflow (FUNCTIONAL.md §8.1): which version was simulated, how many inputs were
/// tried, and what broke. <see cref="FindingsJson"/> holds the findings (TwinFinding list) as JSON; it contains only the
/// values conditions read, never whole payloads. The latest few runs per workflow are kept.
/// </summary>
public class WorkflowTwinRun : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>The production version simulated; null when the workflow has none and the saved draft was used.</summary>
    public Guid? WorkflowVersionId { get; set; }

    /// <summary>"production" or "draft" — what was simulated, for the UI.</summary>
    public string VersionLabel { get; set; } = "draft";

    /// <summary>"manual" (someone pressed Run) or "scheduled" (the background job).</summary>
    public string Trigger { get; set; } = "manual";

    /// <summary>passed, warnings, breaks, or failed (the workflow couldn't be simulated, e.g. invalid YAML).</summary>
    public string Status { get; set; } = "passed";

    public int CasesRun { get; set; }
    public int RoutersTested { get; set; }
    public int BreakCount { get; set; }
    public int WarningCount { get; set; }
    public string FindingsJson { get; set; } = "[]";
    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
    public DateTime CompletedAt { get; set; }
}
