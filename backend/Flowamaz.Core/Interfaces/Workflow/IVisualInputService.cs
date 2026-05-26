using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IVisualInputService
{
    Task<VisualInputResult> ProcessImageAsync(byte[] imageBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default);
}
