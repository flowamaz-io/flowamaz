using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Library;

/// <summary>
/// Template gallery operations: browse (public), install into a workspace (creates a new
/// WorkflowDefinition from the template YAML), and publish a published workflow as a community
/// template (pending review).
/// </summary>
public interface ITemplateService
{
    /// <summary>Paginated, optionally filtered gallery list. No auth required.</summary>
    Task<TemplatePage> ListTemplatesAsync(
        string? category, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Full template detail incl. YAML + node summary, or null if not found / inactive.</summary>
    Task<TemplateDetail?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new WorkflowDefinition in the workspace from the template's YAML and atomically
    /// increments the template's install_count. Returns the new workflow id (for canvas navigation).
    /// </summary>
    Task<Guid> InstallTemplateAsync(
        Guid templateId, Guid workspaceId, Guid userId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a published workflow as a community template (pending review). Throws if the
    /// workflow is not in Published status.
    /// </summary>
    Task<PublishTemplateResult> PublishTemplateAsync(
        Guid workflowId, Guid workspaceId, Guid userId, Guid orgId, PublishTemplateDetails details,
        CancellationToken cancellationToken = default);
}
