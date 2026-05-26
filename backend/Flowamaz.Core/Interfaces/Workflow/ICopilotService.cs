using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface ICopilotService
{
    Task<CopilotResult> ProcessCommandAsync(string command, string? yamlContent, Guid workspaceId, string userId, CancellationToken cancellationToken = default);
}
