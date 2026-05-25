namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when a workflow definition does not exist in the caller's workspace. Maps to 404.</summary>
public sealed class WorkflowNotFoundException : AppException
{
    public WorkflowNotFoundException(Guid workflowDefinitionId)
        : base("WORKFLOW_NOT_FOUND",
            $"Workflow '{workflowDefinitionId}' was not found in this workspace. Check the id, or create the workflow first.",
            httpStatusCode: 404)
    { }
}
