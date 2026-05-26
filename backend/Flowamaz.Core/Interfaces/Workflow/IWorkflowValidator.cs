using Flowamaz.Core.Workflow;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IWorkflowValidator
{
    Task<ValidationResult> ValidateAsync(string yamlContent, Guid? workspaceId = null, CancellationToken ct = default);
}
