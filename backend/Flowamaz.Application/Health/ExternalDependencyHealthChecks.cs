using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

/// <summary>
/// Anthropic probe (O6). Reachability alone said "healthy" while every AI call failed with 401, so the
/// platform key itself is validated with the free <c>GET /v1/models?limit=1</c> call (no tokens spent),
/// cached so /health never calls the provider more than once per <see cref="CacheTtl"/>. Non-critical:
/// a missing/rejected key or an unreachable host reports Degraded (HTTP 200) with a detail line.
/// </summary>
public sealed class AnthropicHealthCheck : IHealthCheck
{
    public const string HttpClientName = "health-anthropic";
    private const string ModelsUrl = "https://api.anthropic.com/v1/models?limit=1";
    private const string AnthropicVersion = "2023-06-01";

    /// <summary>How long a key-validation result is reused before the next probe.</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureCacheTtl = TimeSpan.FromMinutes(1);

    private static readonly object CacheLock = new();
    private static (string KeyHash, HealthCheckResult Result, DateTime ExpiresAt)? _cache;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly AiOptions _ai;
    private readonly ILogger<AnthropicHealthCheck> _logger;

    public AnthropicHealthCheck(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        IOptions<AiOptions> aiOptions,
        ILogger<AnthropicHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _ai = aiOptions.Value;
        _logger = logger;
    }

    /// <summary>Test seam: forget the cached validation result.</summary>
    internal static void ResetCache()
    {
        lock (CacheLock) _cache = null;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync entry");

        // Integration tests never make live network calls.
        if (_environment.IsEnvironment("Test"))
        {
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit stubbed (Test)");
            return HealthCheckResult.Healthy("Not checked: Anthropic probe is skipped in the Test environment.");
        }

        if (_ai.UseStubCompletion)
        {
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit stub completions");
            return HealthCheckResult.Healthy("Not checked: Ai__UseStubCompletion is on, so AI calls return local stub output.");
        }

        if (string.IsNullOrWhiteSpace(_ai.AnthropicPlatformKey))
        {
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit no platform key");
            return _ai.AllowStubWithoutKey
                ? HealthCheckResult.Healthy("No platform key configured: AI calls return local stub output (Ai__AllowStubWithoutKey).")
                : HealthCheckResult.Degraded(
                    "No platform key configured: set Ai__AnthropicPlatformKey (or a workspace BYOK key) to enable AI features.");
        }

        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_ai.AnthropicPlatformKey)));
        lock (CacheLock)
        {
            if (_cache is { } cached && cached.KeyHash == keyHash && cached.ExpiresAt > DateTime.UtcNow)
            {
                _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit cached status={Status}", cached.Result.Status);
                return cached.Result;
            }
        }

        HealthCheckResult result;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HealthProbeRuntime.ProbeTimeout);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
            request.Headers.TryAddWithoutValidation("x-api-key", _ai.AnthropicPlatformKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            using var response = await client.SendAsync(request, cts.Token);
            var status = (int)response.StatusCode;
            result = response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Platform key accepted by Anthropic.")
                : status is 401 or 403
                    ? HealthCheckResult.Degraded(
                        $"Platform key rejected by Anthropic (HTTP {status}): AI features will fail. Set a valid Ai__AnthropicPlatformKey and restart the API.")
                    : HealthCheckResult.Degraded($"Anthropic API returned HTTP {status} when validating the platform key.");
            _logger.LogDebug("AnthropicHealthCheck.CheckHealthAsync exit status={Status}", status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "AnthropicHealthCheck.CheckHealthAsync error, reporting degraded");
            result = HealthCheckResult.Degraded("Anthropic API host unreachable: the platform key could not be validated.", ex);
        }

        lock (CacheLock)
        {
            _cache = (keyHash, result, DateTime.UtcNow.Add(result.Status == HealthStatus.Healthy ? CacheTtl : FailureCacheTtl));
        }
        return result;
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
