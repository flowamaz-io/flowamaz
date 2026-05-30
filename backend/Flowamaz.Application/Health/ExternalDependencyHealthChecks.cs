using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Health;

/// <summary>
/// Health probes for third-party dependencies (Stripe, Anthropic, Resend).
///
/// These are deliberately NON-CRITICAL: a slow or unreachable third party reports
/// <see cref="HealthStatus.Degraded"/> (HTTP 200) rather than <see cref="HealthStatus.Unhealthy"/>
/// (HTTP 503). Only the database and Redis are critical to the platform's own availability, so
/// only those can flip /health to 503. This keeps /health green in CI / sandboxes where outbound
/// network calls to third parties may be blocked.
///
/// In Test/Development environments the probes short-circuit to Healthy so unit and integration
/// tests never make live network calls.
/// </summary>
internal static class HealthProbeRuntime
{
    /// <summary>Short timeout so a hung third party never delays the overall /health response.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>True when live third-party probes should be skipped (returns Healthy immediately).</summary>
    internal static bool ShouldStub(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test");
}

/// <summary>Probes Stripe's public status summary endpoint. Non-critical.</summary>
public sealed class StripeHealthCheck : IHealthCheck
{
    public const string HttpClientName = "health-stripe";
    private const string StatusUrl = "https://status.stripe.com/api/v2/summary.json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StripeHealthCheck> _logger;

    public StripeHealthCheck(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<StripeHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("StripeHealthCheck.CheckHealthAsync entry");
        if (HealthProbeRuntime.ShouldStub(_environment))
        {
            _logger.LogDebug("StripeHealthCheck.CheckHealthAsync exit stubbed");
            return HealthCheckResult.Healthy("Stripe probe stubbed in non-production environment.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HealthProbeRuntime.ProbeTimeout);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(StatusUrl, cts.Token);
            _logger.LogDebug("StripeHealthCheck.CheckHealthAsync exit status={Status}", response.StatusCode);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Stripe status page reachable.")
                : HealthCheckResult.Degraded($"Stripe status page returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            // Non-critical: degraded (HTTP 200), never unhealthy (HTTP 503).
            _logger.LogWarning(ex, "StripeHealthCheck.CheckHealthAsync error — reporting degraded");
            return HealthCheckResult.Degraded("Stripe status page unreachable.", ex);
        }
    }
}

/// <summary>Lightweight reachability probe for the Anthropic API host. Non-critical.</summary>
public sealed class AnthropicHealthCheck : IHealthCheck
{
    public const string HttpClientName = "health-anthropic";
    // HEAD against the API host — avoids spending tokens / requiring a valid key for a liveness ping.
    private const string PingUrl = "https://api.anthropic.com/v1/models";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AnthropicHealthCheck> _logger;

    public AnthropicHealthCheck(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<AnthropicHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync entry");
        if (HealthProbeRuntime.ShouldStub(_environment))
        {
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit stubbed");
            return HealthCheckResult.Healthy("Anthropic probe stubbed in non-production environment.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HealthProbeRuntime.ProbeTimeout);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            // An unauthenticated 401/403 still proves the host is up — treat any HTTP response as reachable.
            using var request = new HttpRequestMessage(HttpMethod.Head, PingUrl);
            using var response = await client.SendAsync(request, cts.Token);
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit status={Status}", response.StatusCode);
            return HealthCheckResult.Healthy("Anthropic API host reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AnthropicHealthCheck.CheckHealthAsync error — reporting degraded");
            return HealthCheckResult.Degraded("Anthropic API host unreachable.", ex);
        }
    }
}

/// <summary>Reachability probe for the Resend API host. Non-critical.</summary>
public sealed class ResendHealthCheck : IHealthCheck
{
    public const string HttpClientName = "health-resend";
    private const string PingUrl = "https://api.resend.com/domains";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ResendHealthCheck> _logger;

    public ResendHealthCheck(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<ResendHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ResendHealthCheck.CheckHealthAsync entry");
        if (HealthProbeRuntime.ShouldStub(_environment))
        {
            _logger.LogDebug("ResendHealthCheck.CheckHealthAsync exit stubbed");
            return HealthCheckResult.Healthy("Resend probe stubbed in non-production environment.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HealthProbeRuntime.ProbeTimeout);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            // 401 (no key on the probe client) still proves the domain/host is up.
            using var request = new HttpRequestMessage(HttpMethod.Head, PingUrl);
            using var response = await client.SendAsync(request, cts.Token);
            _logger.LogDebug("ResendHealthCheck.CheckHealthAsync exit status={Status}", response.StatusCode);
            return HealthCheckResult.Healthy("Resend API host reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResendHealthCheck.CheckHealthAsync error — reporting degraded");
            return HealthCheckResult.Degraded("Resend API host unreachable.", ex);
        }
    }
}
