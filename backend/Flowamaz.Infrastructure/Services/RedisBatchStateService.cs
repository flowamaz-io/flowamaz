using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Tracks pending Anthropic batch IDs in a Redis Hash (key "aibatch:pending", field = batchId).
/// Survives application restarts so the poller can always find outstanding requests.
/// </summary>
public sealed class RedisBatchStateService : IAiBatchStateService
{
    private const string HashKey = "aibatch:pending";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBatchStateService> _logger;

    public RedisBatchStateService(IConnectionMultiplexer redis, ILogger<RedisBatchStateService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task StoreAsync(BatchPendingEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("RedisBatchStateService.StoreAsync batchId={BatchId}", entry.BatchId);
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(entry);
            await db.HashSetAsync(HashKey, entry.BatchId, json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RedisBatchStateService.StoreAsync error batchId={BatchId}", entry.BatchId);
        }
    }

    public async Task<IReadOnlyList<BatchPendingEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("RedisBatchStateService.GetAllAsync enter");
        try
        {
            var db = _redis.GetDatabase();
            var all = await db.HashGetAllAsync(HashKey);
            var result = new List<BatchPendingEntry>(all.Length);
            foreach (var entry in all)
            {
                if (!entry.Value.HasValue) continue;
                var item = JsonSerializer.Deserialize<BatchPendingEntry>(entry.Value.ToString());
                if (item is not null) result.Add(item);
            }
            _logger.LogDebug("RedisBatchStateService.GetAllAsync exit count={Count}", result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RedisBatchStateService.GetAllAsync error — returning empty list");
            return [];
        }
    }

    public async Task RemoveAsync(string batchId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("RedisBatchStateService.RemoveAsync batchId={BatchId}", batchId);
        try
        {
            var db = _redis.GetDatabase();
            await db.HashDeleteAsync(HashKey, batchId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RedisBatchStateService.RemoveAsync error batchId={BatchId}", batchId);
        }
    }
}
