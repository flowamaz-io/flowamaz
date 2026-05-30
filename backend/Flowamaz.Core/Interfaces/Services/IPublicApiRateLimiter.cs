namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Outcome of a public-API rate-limit check, used to populate the X-RateLimit-* headers.</summary>
public sealed record PublicApiRateLimit(bool Allowed, int Limit, int Remaining, long ResetUnixSeconds);

/// <summary>
/// Fixed 1-hour-window rate limiter for the public API, keyed per API key (bucket).
/// Separate from the internal <see cref="IRateLimitService"/> so external developer traffic
/// has its own limits and surfaces remaining/reset for the response headers.
/// </summary>
public interface IPublicApiRateLimiter
{
    Task<PublicApiRateLimit> CheckAsync(string bucket, int maxPerHour, CancellationToken cancellationToken = default);
}
