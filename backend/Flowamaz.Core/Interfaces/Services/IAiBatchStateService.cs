namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Redis-backed store for Anthropic batch request IDs submitted for F5 (Process Intelligence).
/// Entries are created when a batch is submitted and removed when results are processed.
/// </summary>
public interface IAiBatchStateService
{
    Task StoreAsync(BatchPendingEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchPendingEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task RemoveAsync(string batchId, CancellationToken cancellationToken = default);
}

/// <summary>Pending Anthropic batch entry — carries enough context for the poller to process results.</summary>
public sealed record BatchPendingEntry(
    string BatchId,
    Guid WorkspaceId,
    Guid WorkflowDefinitionId,
    string CacheKey,
    string ModelId,
    string Provider);
