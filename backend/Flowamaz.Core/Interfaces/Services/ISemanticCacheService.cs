namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Redis semantic dedup cache. Key derived from SHA256(functionId + workspaceId + normalisedCommand).
/// 24h default TTL (FUNCTIONAL.md §5.5 lever 3). Cache hits skip the AI call entirely.
/// </summary>
public interface ISemanticCacheService
{
    /// <summary>Compute the stable cache key for a function + workspace + command tuple.</summary>
    string ComputeKey(string functionId, Guid workspaceId, string command);

    /// <summary>Look up a cached response. Returns null on miss.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Store a response under <paramref name="key"/> with the given TTL.</summary>
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);
}
