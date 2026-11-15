namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Redis-backed fixed-window rate limiter. Used for Co-pilot rate cap
/// (60 calls/user/hour — FUNCTIONAL.md §5.5 lever 7/8) and any other per-key window.
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Atomic check-and-increment for <paramref name="key"/> within
    /// <paramref name="windowSeconds"/>. Returns true if the request is within budget;
    /// false if it would exceed <paramref name="maxCount"/>.
    /// </summary>
    Task<bool> CheckAndIncrementAsync(
        string key,
        int maxCount,
        int windowSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks several limits at once and counts the request against all of them only if every one has room, so a
    /// request refused by one limit doesn't use up a slot in another. Returns the index of the first limit that is
    /// full, or -1 when the request is allowed (and counted).
    /// </summary>
    async Task<int> CheckAndIncrementAllAsync(
        IReadOnlyList<RateLimitCheck> limits, CancellationToken cancellationToken = default)
    {
        // Fallback for simple implementations: sequential, without the all-or-nothing guarantee.
        for (var i = 0; i < limits.Count; i++)
            if (!await CheckAndIncrementAsync(limits[i].Key, limits[i].MaxCount, limits[i].WindowSeconds, cancellationToken))
                return i;
        return -1;
    }
}

/// <summary>One fixed-window limit: at most <paramref name="MaxCount"/> requests per <paramref name="WindowSeconds"/>.</summary>
public sealed record RateLimitCheck(string Key, int MaxCount, int WindowSeconds);
