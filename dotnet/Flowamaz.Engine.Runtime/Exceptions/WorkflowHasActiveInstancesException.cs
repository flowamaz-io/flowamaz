namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when deleting a workflow that still has running/pending instances. Maps to 409.</summary>
public sealed class WorkflowHasActiveInstancesException : AppException
{
    public WorkflowHasActiveInstancesException()
        : base("WORKFLOW_HAS_ACTIVE_INSTANCES",
            "This workflow still has active instances. Cancel or let them finish before deleting it.",
            httpStatusCode: 409)
    { }
}
