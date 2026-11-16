namespace Flowamaz.Core.Exceptions;

/// <summary>
/// An archive or unarchive that doesn't fit the workflow's status (422): only a published workflow can be
/// archived (a draft is deleted instead), only an archived one unarchived, and an archived workflow is
/// unarchived before it is published again.
/// </summary>
public sealed class WorkflowArchiveException : AppException
{
    private WorkflowArchiveException(string code, string message) : base(code, message, httpStatusCode: 422) { }

    public static WorkflowArchiveException NotPublished() => new("WORKFLOW_NOT_PUBLISHED",
        "Only a published workflow can be archived. A draft that you no longer need can be deleted instead.");

    public static WorkflowArchiveException NotArchived() => new("WORKFLOW_NOT_ARCHIVED",
        "This workflow isn't archived, so there is nothing to unarchive.");

    public static WorkflowArchiveException PublishWhileArchived() => new("WORKFLOW_ARCHIVED",
        "This workflow is archived. Unarchive it first (Unarchive on the workflow's page, or Workflows → status filter Archived → Unarchive), then publish.");
}
