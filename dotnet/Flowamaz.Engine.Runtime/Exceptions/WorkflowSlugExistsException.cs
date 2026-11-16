namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when a workflow slug is already taken within the workspace. Maps to 409.</summary>
public sealed class WorkflowSlugExistsException : AppException
{
    public WorkflowSlugExistsException(string slug)
        : base("WORKFLOW_SLUG_TAKEN",
            $"A workflow with slug '{slug}' already exists in this workspace. Choose a different slug.",
            httpStatusCode: 409)
    { }
}
