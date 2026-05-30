using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flowamaz.Application.Health;

/// <summary>One dependency's status plus how long its probe took.</summary>
public sealed record DependencyHealth(string Status, double ResponseMs);

/// <summary>The full /health response body — status, build metadata, and per-dependency probes.</summary>
public sealed record HealthSummary(
    string Status,
    string Version,
    string Environment,
    double TotalDurationMs,
    IReadOnlyDictionary<string, DependencyHealth> Dependencies);

/// <summary>
/// Composes a <see cref="HealthReport"/> (produced by the registered health checks) into the rich
/// response shape /health returns: overall status, build version, environment, total duration, and a
/// map of each dependency to its status + response time.
///
/// Critical vs non-critical is decided by the checks themselves: database + Redis return Unhealthy
/// when down (→ overall Unhealthy → HTTP 503); third-party probes (Stripe/Anthropic/Resend) only ever
/// return Degraded when unreachable, which keeps the overall status at worst Degraded (→ HTTP 200).
/// </summary>
public static class HealthCheckService
{
    public static HealthSummary Compose(HealthReport report, string version, string environment)
    {
        var dependencies = new Dictionary<string, DependencyHealth>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entry) in report.Entries)
        {
            dependencies[name] = new DependencyHealth(
                ToWireStatus(entry.Status),
                Math.Round(entry.Duration.TotalMilliseconds, 2));
        }

        return new HealthSummary(
            Status: ToWireStatus(report.Status),
            Version: version,
            Environment: environment,
            TotalDurationMs: Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            Dependencies: dependencies);
    }

    /// <summary>HTTP status for a report: only Unhealthy is 503; Degraded (non-critical down) is 200.</summary>
    public static int ToHttpStatusCode(HealthStatus status) =>
        status == HealthStatus.Unhealthy ? 503 : 200;

    private static string ToWireStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };
}
