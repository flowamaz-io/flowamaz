namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when triggering a workflow that has no production version. Maps to 409.</summary>
public sealed class WorkflowNotPublishedException : AppException
{
    public WorkflowNotPublishedException(Guid workflowDefinitionId)
        : base("WORKFLOW_NOT_PUBLISHED",
            $"Workflow '{workflowDefinitionId}' has no published production version, so it cannot be triggered. Publish a version first.",
            httpStatusCode: 409)
    { }
}
