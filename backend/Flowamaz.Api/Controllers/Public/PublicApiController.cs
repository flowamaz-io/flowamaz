using Flowamaz.Core.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>
/// Base for the public, developer-facing API at <c>/api/public/v1</c>. Unlike the internal API it
/// is NOT wrapped by <c>ResponseWrapperMiddleware</c> (the <c>/api/public</c> prefix is skipped):
/// responses are a clean <c>{ data, meta }</c> envelope with snake_case fields, errors are
/// <c>{ error: { code, message, docs } }</c>. Authentication is a workspace API key
/// (<c>Authorization: Bearer fmz_…</c>) only — never a JWT cookie. Rate limits are per API key
/// (proxied by workspace) and surface X-RateLimit-* headers on every response.
/// </summary>
[ApiController]
public abstract class PublicApiController : ControllerBase
{
    /// <summary>Rate-limit buckets. Each has its own hourly budget per workspace (see <see cref="PublicApiLimits"/>).</summary>
    protected const string ReadBucket = "read";
    protected const string TriggerBucket = "trigger";
    protected const string DecideBucket = "decide";

    protected static readonly JsonSerializerOptions PublicJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly ICurrentUserService _currentUser;
    private readonly IPublicApiRateLimiter _rateLimiter;

    protected PublicApiController(ICurrentUserService currentUser, IPublicApiRateLimiter rateLimiter)
    {
        _currentUser = currentUser;
        _rateLimiter = rateLimiter;
    }

    /// <summary>The API key's workspace, or null when the request did not authenticate with an API key.</summary>
    protected Guid? WorkspaceId => _currentUser.IsApiKey ? _currentUser.ApiKeyWorkspaceId : null;

    protected IActionResult Data(object? data, object? meta = null, int statusCode = StatusCodes.Status200OK) =>
        new JsonResult(meta is null ? new { data } : new { data, meta })
        {
            SerializerSettings = PublicJson,
            StatusCode = statusCode,
        };

    protected IActionResult ApiError(int statusCode, string code, string message, string docsAnchor) =>
        new JsonResult(new { error = new { code, message, docs = $"https://docs.flowamaz.io/api/{docsAnchor}" } })
        {
            SerializerSettings = PublicJson,
            StatusCode = statusCode,
        };

    protected IActionResult Unauthorized401() =>
        new JsonResult(new
        {
            error = new
            {
                code = "unauthorized",
                message = "API key required. Generate one at app.flowamaz.io/settings/api-keys",
                docs = "https://docs.flowamaz.io/authentication",
            },
        })
        {
            SerializerSettings = PublicJson,
            StatusCode = StatusCodes.Status401Unauthorized,
        };

    /// <summary>
    /// Resolves the workspace (401 if no API key) and enforces the rate limit (429 if exceeded),
    /// always setting the X-RateLimit-* headers. Returns null when the request may proceed.
    /// </summary>
    protected async Task<IActionResult?> GuardAsync(string requiredScope, string bucketSuffix, CancellationToken ct)
    {
        if (WorkspaceId is not { } ws)
            return Unauthorized401();

        // FUNCTIONAL.md §4.6: every call is limited to the key's scopes (a read-only key must not
        // be able to start runs or decide approvals).
        if (!_currentUser.ApiKeyScopes.Contains(requiredScope, StringComparer.Ordinal))
            return ApiError(StatusCodes.Status403Forbidden, "insufficient_scope",
                $"This API key doesn't have the '{requiredScope}' scope. Create a key with that scope in " +
                "Settings → API keys and use it for this call.", "errors#insufficient_scope");

        var services = HttpContext?.RequestServices;
        var limits = services?.GetService<IOptions<PublicApiLimits>>()?.Value ?? new PublicApiLimits();

        // 1. The plan's per-minute limit for the whole organisation (FUNCTIONAL.md §13.2).
        PublicApiRateLimit? minute = null;
        PlanApiLimit? plan = null;
        if (limits.EnforcePlanRateLimits && services?.GetService<IPublicApiPlanLimits>() is { } planLimits)
        {
            plan = await planLimits.ResolveAsync(ws, ct);
            if (plan is { PerMinute: > 0 })
                minute = await _rateLimiter.CheckAsync($"org:{plan.OrgId}:minute", plan.PerMinute, windowSeconds: 60, ct);
        }

        // 2. The workspace's hourly budget for this kind of call.
        var hour = minute is { Allowed: false } ? null
            : await _rateLimiter.CheckAsync($"{ws}:{bucketSuffix}", limits.PerHour(bucketSuffix), ct);

        WriteRateLimitHeaders(minute, hour);
        if ((minute is { Allowed: false } ? minute : hour is { Allowed: false } ? hour : null) is { } blocked)
            Response.Headers.RetryAfter = Math.Max(1, blocked.ResetUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();

        if (minute is { Allowed: false })
            return ApiError(StatusCodes.Status429TooManyRequests, "rate_limited", MinuteLimitMessage(plan!), "errors#rate_limited");
        if (hour is { Allowed: false })
            return ApiError(StatusCodes.Status429TooManyRequests, "rate_limited",
                $"This workspace has used its hourly budget for {BucketDescription(bucketSuffix)} ({hour.Limit} an hour). " +
                "Wait until the time in X-RateLimit-Reset before retrying.", "errors#rate_limited");

        return null;
    }

    /// <summary>
    /// X-RateLimit-* report whichever limit is closer to blocking (what a client should back off on);
    /// X-RateLimit-Minute-* (plan) and X-RateLimit-Hour-* (workspace budget) give both.
    /// </summary>
    private void WriteRateLimitHeaders(PublicApiRateLimit? minute, PublicApiRateLimit? hour)
    {
        if (minute is not null)
        {
            Response.Headers["X-RateLimit-Minute-Limit"] = minute.Limit.ToString();
            Response.Headers["X-RateLimit-Minute-Remaining"] = minute.Remaining.ToString();
        }
        if (hour is not null)
        {
            Response.Headers["X-RateLimit-Hour-Limit"] = hour.Limit.ToString();
            Response.Headers["X-RateLimit-Hour-Remaining"] = hour.Remaining.ToString();
        }

        var binding = (minute, hour) switch
        {
            ({ } m, { } h) => m.Remaining <= h.Remaining ? m : h,
            ({ } m, null) => m,
            (null, { } h) => h,
            _ => null,
        };
        if (binding is null) return;
        Response.Headers["X-RateLimit-Limit"] = binding.Limit.ToString();
        Response.Headers["X-RateLimit-Remaining"] = binding.Remaining.ToString();
        Response.Headers["X-RateLimit-Reset"] = binding.ResetUnixSeconds.ToString();
    }

    /// <summary>
    /// A figure agreed for the organisation isn't the plan's, and upgrading wouldn't change it, so it is quoted as
    /// the organisation's own limit with the way to raise it.
    /// </summary>
    internal static string MinuteLimitMessage(PlanApiLimit plan) => plan.IsCustom
        ? $"Your organisation's limit is {plan.PerMinute} API calls a minute. Wait until the time in X-RateLimit-Reset " +
          "before retrying. To raise the limit, contact Flowamaz support."
        : $"Your {plan.PlanName} plan allows {plan.PerMinute} API calls a minute across your organisation. " +
          "Wait until the time in X-RateLimit-Reset, or upgrade your plan in Settings → Billing for a higher limit.";

    private static string BucketDescription(string bucket) => bucket switch
    {
        TriggerBucket => "starting runs",
        DecideBucket => "decisions and cancellations",
        _ => "reads",
    };

    protected async Task<string> ReadRawBodyAsync(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;
        return body;
    }

    protected static (int Page, int PerPage) ReadPaging(int? page, int? perPage) =>
        (Math.Max(1, page ?? 1), Math.Clamp(perPage ?? 20, 1, 100));

    protected static object PageMeta(int page, int perPage, int total) =>
        new { page, per_page = perPage, total };
}

/// <summary>
/// Hourly public-API budgets per workspace, one per bucket (section <c>PublicApi</c>). Gate decisions have their
/// own bucket: an application that runs its approvals through the API makes several decisions per run, and those
/// must not use up the budget for starting runs.
/// </summary>
public sealed class PublicApiLimits
{
    public const string Section = "PublicApi";

    /// <summary>Reads: workflows, runs, events, pending gates.</summary>
    public int ReadPerHour { get; set; } = 1000;

    /// <summary>Starting runs.</summary>
    public int TriggerPerHour { get; set; } = 100;

    /// <summary>Deciding gates and cancelling runs.</summary>
    public int DecidePerHour { get; set; } = 1000;

    /// <summary>
    /// Apply each plan's per-minute limit (Community 10, Starter 60, Pro 300, Enterprise custom). Turn off only for
    /// local testing against an org on the Community plan.
    /// </summary>
    public bool EnforcePlanRateLimits { get; set; } = true;

    public int PerHour(string bucket) => bucket switch
    {
        "trigger" => TriggerPerHour,
        "decide" => DecidePerHour,
        _ => ReadPerHour,
    };
}
