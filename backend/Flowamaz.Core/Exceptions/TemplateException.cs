namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Template gallery failures (install/publish). Carries an actionable message + the right HTTP
/// status so GlobalExceptionMiddleware maps it without controller-level branching: not-found (404),
/// unpublished workflow / invalid YAML (422), and missing required fields (400).
/// </summary>
public sealed class TemplateException : AppException
{
    public TemplateException(int httpStatusCode, string errorCode, string message)
        : base(errorCode, message, httpStatusCode)
    {
    }

    public static TemplateException NotFound(Guid templateId) => new(
        404, "template_not_found",
        $"Template '{templateId}' was not found or is no longer available. Refresh the gallery and try again.");

    public static TemplateException WorkflowNotFound(Guid workflowId) => new(
        404, "template_not_found",
        $"Workflow '{workflowId}' was not found in this workspace. You can only publish a workflow you own.");

    public static TemplateException WorkflowNotPublished() => new(
        422, "workflow_not_published",
        "Publish the workflow before submitting as a template.");

    public static TemplateException InvalidYaml(string firstError) => new(
        422, "invalid_yaml",
        $"Template YAML failed validation. Fix the workflow, then publish again. First error: {firstError}");

    public static TemplateException Validation(string message) => new(
        400, "validation_error", message);
}
