namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Outcome of a public-API rate-limit check, used to populate the X-RateLimit-* headers.</summary>
public sealed record PublicApiRateLimit(bool Allowed, int Limit, int Remaining, long ResetUnixSeconds);

/// <summary>
/// Fixed-window rate limiter for the public API, keyed per bucket (hourly per-workspace budgets, per-minute plan limits).
/// Separate from the internal <see cref="IRateLimitService"/> so external developer traffic
/// has its own limits and surfaces remaining/reset for the response headers.
/// </summary>
public interface IPublicApiRateLimiter
{
    Task<PublicApiRateLimit> CheckAsync(string bucket, int maxPerHour, CancellationToken cancellationToken = default)
        => CheckAsync(bucket, maxPerHour, windowSeconds: 3600, cancellationToken);

    /// <summary>Counts one call in a fixed window of <paramref name="windowSeconds"/> for <paramref name="bucket"/>.</summary>
    Task<PublicApiRateLimit> CheckAsync(string bucket, int max, int windowSeconds, CancellationToken cancellationToken = default);
}
