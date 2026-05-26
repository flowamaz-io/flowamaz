using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IConversationImportService
{
    Task<ConversationImportResult> ImportAsync(string conversationText, string sourceType, Guid workspaceId, CancellationToken cancellationToken = default);
}
