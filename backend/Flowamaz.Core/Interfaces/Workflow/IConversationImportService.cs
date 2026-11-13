using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IConversationImportService
{
    /// <summary>Step 1: read the process out of the conversation (one metered AI call). Nothing is saved.</summary>
    Task<ConversationPreviewResult> ExtractAsync(string conversationText, string sourceType, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Step 2: generate the workflow YAML from an extraction the user reviewed (and may have edited).</summary>
    Task<ConversationImportResult> GenerateAsync(ConversationExtraction confirmed, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Both steps without a review, for API clients.</summary>
    Task<ConversationImportResult> ImportAsync(string conversationText, string sourceType, Guid workspaceId, CancellationToken cancellationToken = default);
}
