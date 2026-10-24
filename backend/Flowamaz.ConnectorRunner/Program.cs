using System.Net;
using System.Text.Json;
using Flowamaz.Application.Connectors;
using Flowamaz.Application.Connectors.Handlers;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.ConnectorRunner;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Flowamaz connector runner (FUNCTIONAL.md §9.5). Runs connector operations that call third parties
// or execute code, in a separate container with:
//   • no database, Redis or workspace data — each request carries only one operation's inputs and
//     its one decrypted credential;
//   • network access only through the egress proxy (HTTPS_PROXY), which refuses private, loopback,
//     link-local/metadata and other internal addresses; plus a per-connector domain allowlist here;
//   • HMAC-signed requests from the API (shared secret, 60s replay window).
// ─────────────────────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

var proxyUrl = builder.Configuration["Runner:EgressProxy"]
    ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
    ?? Environment.GetEnvironmentVariable("https_proxy");

// Outbound pipeline for every connector client: domain allowlist → (egress proxy | in-process guard).
foreach (var client in new[]
         {
             HttpRestConnectorHandlerBase.HttpClientName, SlackHandlerBase.HttpClientName,
             TeamsSendMessageHandler.HttpClientName, GitHubHandlerBase.HttpClientName,
             WebhookEmitHandler.HttpClientName, Microsoft365HandlerBase.HttpClientName,
         })
{
    builder.Services.AddHttpClient(client)
        .AddHttpMessageHandler(() => new DomainAllowlistHandler())
        .ConfigurePrimaryHttpMessageHandler(() => string.IsNullOrWhiteSpace(proxyUrl)
            // No proxy configured (local dev): fall back to the connect-time IP guard.
            ? EgressGuard.CreateHandler(allowPrivateNetworks: builder.Configuration.GetValue<bool>(EgressGuard.ConfigKey))
            : new SocketsHttpHandler
            {
                Proxy = new WebProxy(proxyUrl) { BypassProxyOnLocal = false },
                UseProxy = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });
}

builder.Services.AddScoped<ConnectorCallContext>();
// Only connectors that talk to third parties or run code live here (catalog: Sandboxed = true).
builder.Services.AddScoped<IConnectorOperationHandler, HttpGetHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, HttpPostHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, HttpPutHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, HttpPatchHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, HttpDeleteHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, HttpHeadHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, SlackSendMessageHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, SlackSendDmHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, SlackPostApprovalMessageHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, TeamsSendMessageHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, TeamsSendWebhookMessageHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, TeamsPostAdaptiveCardHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, GitHubCreateIssueHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, GitHubGetIssueHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, GitHubCreatePrHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, GitHubAddCommentHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, WebhookEmitHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, ScriptShellRunHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, Microsoft365SendMailHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, Microsoft365CreateEventHandler>();
builder.Services.AddScoped<IConnectorOperationHandler, Microsoft365UploadFileHandler>();

var maxConcurrency = Math.Max(1, builder.Configuration.GetValue("Runner:MaxConcurrency", 16));
builder.Services.AddSingleton(new SemaphoreSlim(maxConcurrency, maxConcurrency));

var app = builder.Build();

var secret = app.Configuration["Runner:SharedSecret"] ?? app.Configuration["CONNECTOR_RUNNER_SECRET"];
if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
{
    app.Logger.LogCritical("CONNECTOR_RUNNER_SECRET must be set (32+ characters) and match the API. Refusing to start.");
    throw new InvalidOperationException("CONNECTOR_RUNNER_SECRET must be set (32+ characters).");
}
if (string.IsNullOrWhiteSpace(proxyUrl))
    app.Logger.LogWarning("No egress proxy configured (HTTPS_PROXY / Runner:EgressProxy) — using the in-process IP guard only.");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/execute", async (HttpContext http, IServiceProvider services, SemaphoreSlim gate, ILogger<RunnerLog> log) =>
{
    using var reader = new StreamReader(http.Request.Body);
    var body = await reader.ReadToEndAsync(http.RequestAborted);
    if (!RunnerSignature.Verify(secret, http.Request.Headers[RunnerSignature.TimestampHeader],
            http.Request.Headers[RunnerSignature.SignatureHeader], body, DateTimeOffset.UtcNow))
    {
        log.LogWarning("Runner: rejected unsigned or expired request from {Remote}", http.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    ConnectorRunRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<ConnectorRunRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Malformed request." });
    }
    if (request is null) return Results.BadRequest(new { error = "Empty request." });

    var spec = OfficialConnectorCatalog.FindConnector(request.ConnectorId);
    if (spec is not { Sandboxed: true })
        return Results.Ok(new ConnectorRunResponse(false, null, $"{request.ConnectorId} is not run in the sandbox.", false));

    var handler = services.GetServices<IConnectorOperationHandler>()
        .FirstOrDefault(h => h.ConnectorId == request.ConnectorId && h.OperationId == request.OperationId);
    if (handler is null)
        return Results.Ok(new ConnectorRunResponse(false, null, $"Unknown operation {request.ConnectorId}/{request.OperationId}.", false));

    if (!await gate.WaitAsync(TimeSpan.FromSeconds(10), http.RequestAborted))
        return Results.Ok(new ConnectorRunResponse(false, null, "The connector sandbox is busy; the step will be retried.", true));

    var op = $"{request.ConnectorId}/{request.OperationId}";
    try
    {
        var context = services.GetRequiredService<ConnectorCallContext>();
        context.WorkspaceId = request.WorkspaceId;
        context.InstanceId = request.InstanceId;
        context.NodeId = request.NodeId;
        // The catalog is the authority for where a connector may go; the request cannot widen it.
        RunnerCallScope.AllowedDomains = spec.AllowedDomains;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 300)));

        using var input = JsonDocument.Parse(request.Input.GetRawText());
        var credential = request.Credential is { } c ? new ConnectorCredential(c.Id, c.Name, c.AuthType, c.Secret) : null;

        log.LogInformation("Runner: executing {Op} workspace={WorkspaceId} node={NodeId}", op, request.WorkspaceId, request.NodeId);
        using var output = await handler.ExecuteAsync(input, credential, timeout.Token);
        return Results.Ok(new ConnectorRunResponse(true, output.RootElement.Clone(), null, false));
    }
    catch (ConnectorOperationException ex)
    {
        return Results.Ok(new ConnectorRunResponse(false, null, ex.Message, ex.Retryable));
    }
    catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
    {
        return Results.Ok(new ConnectorRunResponse(false, null, $"{op} did not finish within {request.TimeoutSeconds}s.", true));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Never echo exception detail that might contain request material; log type + op only.
        log.LogError("Runner: {Op} failed with {ExceptionType}", op, ex.GetType().Name);
        return Results.Ok(new ConnectorRunResponse(false, null, $"{op} failed unexpectedly in the connector sandbox.", true));
    }
    finally
    {
        RunnerCallScope.AllowedDomains = null;
        gate.Release();
    }
});

app.Run();

/// <summary>Log category for the runner endpoints.</summary>
internal sealed class RunnerLog;

namespace Flowamaz.ConnectorRunner
{
    /// <summary>Assembly marker for WebApplicationFactory in tests (the API also has a global Program).</summary>
    public sealed class ConnectorRunnerEntryPoint;
}
