namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when triggering a workflow that has no production version. Maps to 409.</summary>
public sealed class WorkflowNotPublishedException : AppException
{
    public WorkflowNotPublishedException(Guid workflowDefinitionId)
        : base("WORKFLOW_NOT_PUBLISHED",
            $"Workflow '{workflowDefinitionId}' has no published production version, so it cannot be triggered. Publish a version first.",
            httpStatusCode: 409)
    { }

    private WorkflowNotPublishedException(string code, string message) : base(code, message, httpStatusCode: 409) { }

    /// <summary>The workflow was archived: it keeps its history but takes no new runs.</summary>
    public static WorkflowNotPublishedException Archived(Guid workflowDefinitionId) => new("WORKFLOW_ARCHIVED",
        $"Workflow '{workflowDefinitionId}' is archived, so it can't be run. Unarchive it (Unarchive on the workflow's page, or Workflows → status filter Archived → Unarchive) to run it again.");
}
