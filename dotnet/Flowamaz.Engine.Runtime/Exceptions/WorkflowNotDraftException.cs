namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a delete is attempted on a workflow that is not in Draft status. Only drafts can be
/// deleted; published workflows are archived instead. Maps to 422 Unprocessable Entity.
/// </summary>
public sealed class WorkflowNotDraftException : AppException
{
    public WorkflowNotDraftException()
        : base("WORKFLOW_NOT_DRAFT",
            "Published workflows cannot be deleted. Archive the workflow instead.",
            httpStatusCode: 422)
    { }
}
