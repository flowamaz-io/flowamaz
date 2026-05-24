namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Redis-backed sliding-window rate limiter. Used for Co-pilot rate cap
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
}
