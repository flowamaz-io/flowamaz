using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis semantic dedup cache for Co-pilot and other AI functions. Same normalised command
/// from the same workspace returns the cached response without an AI call
/// (FUNCTIONAL.md §5.5 lever 3). Key = SHA256(functionId + workspaceId + normalisedCommand).
/// </summary>
public sealed class SemanticCacheService : ISemanticCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SemanticCacheService> _logger;

    public SemanticCacheService(IConnectionMultiplexer redis, ILogger<SemanticCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public string ComputeKey(string functionId, Guid workspaceId, string command)
    {
        ArgumentException.ThrowIfNullOrEmpty(functionId);
        ArgumentException.ThrowIfNullOrEmpty(command);

        var normalised = NormaliseCommand(command);
        var payload = $"{functionId}|{workspaceId:N}|{normalised}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"aicache:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("SemanticCacheService.GetAsync enter key={Key}", key);
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync((RedisKey)key);
            var hit = value.HasValue;
            _logger.LogDebug("SemanticCacheService.GetAsync exit key={Key} hit={Hit}", key, hit);
            return hit ? value.ToString() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SemanticCacheService.GetAsync error key={Key} — returning miss", key);
            return null;
        }
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("SemanticCacheService.SetAsync enter key={Key} ttl={Ttl}", key, ttl);
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync((RedisKey)key, value, ttl);
            _logger.LogDebug("SemanticCacheService.SetAsync exit key={Key}", key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SemanticCacheService.SetAsync error key={Key} — cache write dropped", key);
        }
    }

    /// <summary>
    /// Lowercase + trim + collapse whitespace runs to a single space. Keeps the cache
    /// hit-rate high for trivially varying inputs ("Add a timeout" vs "  add a   timeout ").
    /// </summary>
    internal static string NormaliseCommand(string command)
    {
        var trimmed = command.Trim().ToLowerInvariant();
        var sb = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
