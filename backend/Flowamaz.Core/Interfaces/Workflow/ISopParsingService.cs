using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface ISopParsingService
{
    Task<SopParseResult> ParseAsync(byte[] fileBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default);
}
