using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Engine.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps the engine's HTTP endpoints (README.md lists them). Every workspace route asks the host's
/// <see cref="IEngineApiAccess"/> first; business errors come back as <c>{ code, message }</c> with their status.
/// </summary>
public static class EngineApiEndpoints
{
    /// <summary>JSON for requests and responses: camelCase, enums as strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <param name="prefix">Route prefix, e.g. <c>/api/v1</c>. Workspace routes are <c>{prefix}/workspaces/{workspaceId}/…</c>.</param>
    public static RouteGroupBuilder MapFlowamazEngineApi(this IEndpointRouteBuilder app, string prefix = "/api/v1")
    {
        if (app.ServiceProvider.GetService<IEngineApiAccess>() is null)
            throw new InvalidOperationException(
                "The Flowamaz engine API needs an access rule: register an IEngineApiAccess before mapping it " +
                "(for one team, services.AddSingleton<IEngineApiAccess>(new SignedInUserAccess(workspaceId))).");

        var root = app.MapGroup(prefix);
        root.AddEndpointFilter(TranslateErrors);
        var ws = root.MapGroup("/workspaces/{workspaceId:guid}");

        // ── Workflows ─────────────────────────────────────────────────────────
        ws.MapGet("/workflows", (HttpContext h, Guid workspaceId, int? page, int? pageSize, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (_, s) =>
                Ok(Page(await s.GetRequiredService<WorkflowService>().ListAsync(workspaceId, ct), page, pageSize, s))));

        ws.MapPost("/workflows", (HttpContext h, Guid workspaceId, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Author, ct, async (caller, s) =>
            {
                var body = await ReadAsync<CreateWorkflowBody>(h, ct);
                Require(body?.Name, "name", "a name for the workflow, e.g. \"Loan approval\"");
                Require(body!.Slug, "slug", "a short id unique in the workspace, e.g. \"loan-approval\"");
                Require(body.YamlContent, "yamlContent", "the workflow's YAML (docs/yaml-spec.md)");
                var created = await s.GetRequiredService<WorkflowService>().CreateAsync(workspaceId, caller.UserId,
                    new CreateWorkflowDefinitionRequest(body.Name!, body.Slug!, body.YamlContent!, body.Description,
                        WorkflowCreatedByMethod.Canvas, body.SlaThresholdMs), ct);
                return Results.Json(created, Json, statusCode: StatusCodes.Status201Created);
            }));

        ws.MapGet("/workflows/{id:guid}", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (_, s) =>
                OkOrNotFound(await s.GetRequiredService<WorkflowService>().GetByIdAsync(workspaceId, id, ct), "workflow", id)));

        ws.MapPut("/workflows/{id:guid}", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Author, ct, async (_, s) =>
            {
                var body = await ReadAsync<UpdateWorkflowDefinitionRequest>(h, ct);
                Require(body?.YamlContent, "yamlContent", "the workflow's YAML (docs/yaml-spec.md)");
                return OkOrNotFound(await s.GetRequiredService<WorkflowService>()
                    .UpdateAsync(workspaceId, id, body!.YamlContent, body.SlaThresholdMs, ct), "workflow", id);
            }));

        ws.MapDelete("/workflows/{id:guid}", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Author, ct, async (caller, s) =>
            {
                await s.GetRequiredService<WorkflowService>().DeleteAsync(id, workspaceId, caller.UserId, ct);
                return Results.NoContent();
            }));

        ws.MapPost("/workflows/{id:guid}/publish", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Publish, ct, async (caller, s) =>
                OkOrNotFound(await s.GetRequiredService<WorkflowService>().PublishAsync(workspaceId, id, caller.UserId, ct), "workflow", id)));

        ws.MapPost("/workflows/{id:guid}/archive", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Publish, ct, async (caller, s) =>
                Ok(await s.GetRequiredService<WorkflowService>().ArchiveAsync(workspaceId, id, caller.UserId, ct))));

        ws.MapPost("/workflows/{id:guid}/unarchive", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Publish, ct, async (caller, s) =>
                Ok(await s.GetRequiredService<WorkflowService>().UnarchiveAsync(workspaceId, id, caller.UserId, ct))));

        ws.MapGet("/workflows/{id:guid}/versions", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (_, s) =>
                OkOrNotFound(await s.GetRequiredService<WorkflowService>().GetVersionsAsync(workspaceId, id, ct), "workflow", id)));

        ws.MapPost("/workflows/{id:guid}/runs", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Run, ct, async (caller, s) =>
            {
                var body = await ReadAsync<StartRunBody>(h, ct, allowEmpty: true) ?? new StartRunBody(null, false);
                var run = await s.GetRequiredService<IWorkflowOrchestrator>().TriggerAsync(
                    workspaceId, id, PayloadText(body.Payload), IdempotencyKey(h), InstanceTriggerType.Api,
                    isTest: body.IsTest, triggeredByUserId: caller.UserId, cancellationToken: ct);
                return Results.Json(new TriggerInstanceResponse(run.Id, run.Status.ToString(), run.CreatedAt), Json,
                    statusCode: StatusCodes.Status202Accepted);
            }));

        // ── Runs ──────────────────────────────────────────────────────────────
        ws.MapGet("/runs", (HttpContext h, Guid workspaceId, string? status, Guid? workflowId, bool? includeTest,
                int? page, int? pageSize, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (caller, s) =>
            {
                InstanceStatus? filter = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (!Enum.TryParse<InstanceStatus>(status, ignoreCase: true, out var parsed))
                        return Problem(StatusCodes.Status400BadRequest, "INVALID_STATUS",
                            $"'{status}' isn't a run status. Use one of: {string.Join(", ", Enum.GetNames<InstanceStatus>())}.");
                    filter = parsed;
                }
                var runs = await s.GetRequiredService<InstanceService>().ListAsync(workspaceId, filter, workflowId, null, null,
                    includeTest ?? false, ct: ct, onlyTriggeredBy: caller.Runs.OnlyTriggeredBy);
                return Ok(Page(runs.OrderByDescending(r => r.CreatedAt).ToList(), page, pageSize, s));
            }));

        ws.MapGet("/runs/{id:guid}", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (caller, s) =>
                OkOrNotFound(await s.GetRequiredService<InstanceService>().GetDetailAsync(workspaceId, id, ct, caller.Runs), "run", id)));

        ws.MapGet("/runs/{id:guid}/events", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (caller, s) =>
                OkOrNotFound(await s.GetRequiredService<InstanceService>().GetEventsAsync(workspaceId, id, ct, caller.Runs), "run", id)));

        ws.MapGet("/runs/{id:guid}/timeline", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (caller, s) =>
                OkOrNotFound(await s.GetRequiredService<InstanceService>().GetTimelineAsync(workspaceId, id, ct, caller.Runs), "run", id)));

        ws.MapPost("/runs/{id:guid}/cancel", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Operate, ct, async (caller, s) =>
            {
                var runs = s.GetRequiredService<InstanceService>();
                if (await runs.GetDetailAsync(workspaceId, id, ct) is null) return NotFound("run", id);
                await s.GetRequiredService<IWorkflowOrchestrator>().CancelAsync(id, caller.UserId, ct);
                return Ok(await runs.GetDetailAsync(workspaceId, id, ct, caller.Runs));
            }));

        ws.MapPost("/runs/{id:guid}/retry", (HttpContext h, Guid workspaceId, Guid id, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Operate, ct, async (_, s) =>
            {
                var retried = await s.GetRequiredService<InstanceService>().RetryAsync(workspaceId, id, ct);
                return retried is null ? NotFound("run", id) : Results.Json(retried, Json, statusCode: StatusCodes.Status202Accepted);
            }));

        // ── Gates ─────────────────────────────────────────────────────────────
        // status=pending (default): the gates waiting for a decision. status=all: decided ones too, newest first (up to 200).
        ws.MapGet("/gates", (HttpContext h, Guid workspaceId, bool? includeTest, string? status, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (_, s) =>
            {
                var gates = s.GetRequiredService<GateService>();
                if (string.IsNullOrWhiteSpace(status) || status.Equals("pending", StringComparison.OrdinalIgnoreCase))
                    return Ok(await gates.ListPendingAsync(workspaceId, includeTest ?? false, ct));
                if (status.Equals("all", StringComparison.OrdinalIgnoreCase))
                    return Ok(await gates.ListRecentAsync(workspaceId, includeTest ?? false, ct));
                return Problem(StatusCodes.Status400BadRequest, "INVALID_STATUS",
                    $"'{status}' isn't a gate list. Use status=pending (waiting for a decision) or status=all (decided ones too).");
            }));

        ws.MapGet("/gates/{runId:guid}/{nodeId}", (HttpContext h, Guid workspaceId, Guid runId, string nodeId, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Read, ct, async (_, s) =>
                OkOrNotFound(await s.GetRequiredService<GateService>().GetAsync(workspaceId, runId, nodeId, ct), "gate", runId, nodeId)));

        ws.MapPost("/gates/{runId:guid}/{nodeId}/decide", (HttpContext h, Guid workspaceId, Guid runId, string nodeId, CancellationToken ct) =>
            As(h, workspaceId, EngineApiAction.Decide, ct, async (caller, s) =>
            {
                var body = await ReadAsync<GateDecisionRequest>(h, ct);
                Require(body?.Decision, "decision", "\"approved\" or \"rejected\"");
                return OkOrNotFound(await s.GetRequiredService<GateService>().DecideAsync(workspaceId, runId, nodeId,
                    body!.Decision, body.Note, caller.UserId, ct, values: body.Values), "gate", runId, nodeId);
            }));

        // ── Webhook trigger (signed, no sign-in) ─────────────────────────────
        root.MapPost("/hooks/workspaces/{workspaceId:guid}/workflows/{id:guid}", WebhookAsync);

        return root;
    }

    private sealed record CreateWorkflowBody(string? Name, string? Slug, string? YamlContent, string? Description, long? SlaThresholdMs);

    private sealed record StartRunBody(JsonElement? Payload, bool IsTest);

    private const string SignatureHeader = "X-Flowamaz-Signature";

    private static async Task<IResult> WebhookAsync(HttpContext h, Guid workspaceId, Guid id, CancellationToken ct)
    {
        var services = h.RequestServices;
        var options = services.GetRequiredService<IOptions<EngineApiOptions>>().Value;
        var secret = await services.GetRequiredService<IWebhookSecretResolver>().GetSecretAsync(workspaceId, id, ct);
        if (secret is null)
            return Problem(StatusCodes.Status404NotFound, "WEBHOOK_NOT_ENABLED",
                "This workflow doesn't accept webhooks. Set EngineApi:WebhookSecret on the host (or its webhook secret), then sign requests with it.");

        if (h.Request.ContentLength > options.MaxWebhookBodyBytes)
            return Problem(StatusCodes.Status413PayloadTooLarge, "WEBHOOK_TOO_LARGE",
                $"The webhook body is larger than {options.MaxWebhookBodyBytes} bytes. Send less data, or raise EngineApi:MaxWebhookBodyBytes.");
        var body = await ReadBytesAsync(h.Request.Body, options.MaxWebhookBodyBytes, ct);
        if (body is null)
            return Problem(StatusCodes.Status413PayloadTooLarge, "WEBHOOK_TOO_LARGE",
                $"The webhook body is larger than {options.MaxWebhookBodyBytes} bytes. Send less data, or raise EngineApi:MaxWebhookBodyBytes.");

        if (!SignatureMatches(h.Request.Headers[SignatureHeader].ToString(), secret, body))
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Flowamaz.Engine.Api.Webhooks")
                .LogWarning("Webhook signature rejected workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);
            return Problem(StatusCodes.Status401Unauthorized, "WEBHOOK_SIGNATURE_INVALID",
                $"The {SignatureHeader} header is missing or doesn't match. Send sha256=<hex HMAC-SHA256 of the raw body, keyed with the webhook secret>.");
        }

        string? payload = null;
        if (body.Length > 0)
        {
            try { using var _ = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                return Problem(StatusCodes.Status400BadRequest, "WEBHOOK_BODY_NOT_JSON",
                    "The webhook body isn't valid JSON. Send a JSON object; the run reads it as its trigger payload.");
            }
            payload = Encoding.UTF8.GetString(body);
        }

        var run = await services.GetRequiredService<IWorkflowOrchestrator>().TriggerAsync(
            workspaceId, id, payload, IdempotencyKey(h), InstanceTriggerType.Webhook, cancellationToken: ct);
        return Results.Json(new TriggerInstanceResponse(run.Id, run.Status.ToString(), run.CreatedAt), Json,
            statusCode: StatusCodes.Status202Accepted);
    }

    /// <summary>Constant-time check of <c>sha256=&lt;hex&gt;</c> against the body's HMAC-SHA256.</summary>
    internal static bool SignatureMatches(string header, string secret, byte[] body)
    {
        const string scheme = "sha256=";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        byte[] given;
        try { given = Convert.FromHexString(header[scheme.Length..].Trim()); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private static async Task<byte[]?> ReadBytesAsync(Stream stream, int max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static async Task<IResult> As(HttpContext h, Guid workspaceId, EngineApiAction action, CancellationToken ct,
        Func<EngineCaller, IServiceProvider, Task<IResult>> handler)
    {
        var caller = await h.RequestServices.GetRequiredService<IEngineApiAccess>().AuthorizeAsync(h, workspaceId, action, ct);
        if (caller is null)
        {
            return h.User.Identity?.IsAuthenticated == true
                ? Problem(StatusCodes.Status403Forbidden, "ENGINE_ACCESS_DENIED",
                    $"You can't {Describe(action)} in this workspace. Ask the workspace's admin for access.")
                : Problem(StatusCodes.Status401Unauthorized, "SIGN_IN_REQUIRED", "Sign in first, then try again.");
        }
        return await handler(caller, h.RequestServices);
    }

    private static string Describe(EngineApiAction action) => action switch
    {
        EngineApiAction.Read => "see workflows and runs",
        EngineApiAction.Author => "create or edit workflows",
        EngineApiAction.Publish => "publish or archive workflows",
        EngineApiAction.Run => "start runs",
        EngineApiAction.Operate => "cancel or retry runs",
        EngineApiAction.Decide => "decide approvals",
        _ => "do that",
    };

    private static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AppException ex)
        {
            return Problem(ex.HttpStatusCode, ex.ErrorCode, ex.Message);
        }
        catch (BadRequestException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, ex.Code, ex.Message);
        }
    }

    private sealed class BadRequestException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private static void Require(string? value, string field, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("FIELD_REQUIRED", $"'{field}' is required: {what}.");
    }

    private static async Task<T?> ReadAsync<T>(HttpContext h, CancellationToken ct, bool allowEmpty = false) where T : class
    {
        using var reader = new StreamReader(h.Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return allowEmpty ? null : throw new BadRequestException("BODY_REQUIRED", "Send a JSON body with this request.");
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException("INVALID_JSON",
                $"The request body isn't valid JSON for this endpoint ({ex.Message}). Check the field names and types in the API reference.");
        }
    }

    private static string? PayloadText(JsonElement? payload) =>
        payload is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } p ? p.GetRawText() : null;

    private static string? IdempotencyKey(HttpContext h)
    {
        var key = h.Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    private static object Page<T>(IReadOnlyList<T> items, int? page, int? pageSize, IServiceProvider services)
    {
        var max = services.GetRequiredService<IOptions<EngineApiOptions>>().Value.MaxPageSize;
        var size = Math.Clamp(pageSize ?? 20, 1, max);
        var number = Math.Max(page ?? 1, 1);
        return new { items = items.Skip((number - 1) * size).Take(size).ToList(), total = items.Count, page = number, pageSize = size };
    }

    private static IResult Ok(object? value) => Results.Json(value, Json);

    private static IResult OkOrNotFound(object? value, string what, Guid id, string? nodeId = null) =>
        value is null ? NotFound(what, id, nodeId) : Ok(value);

    private static IResult NotFound(string what, Guid id, string? nodeId = null) =>
        Problem(StatusCodes.Status404NotFound, $"{what.ToUpperInvariant()}_NOT_FOUND",
            $"No {what} '{id}{(nodeId is null ? "" : "/" + nodeId)}' in this workspace (or you can't see it). Check the id.");

    private static IResult Problem(int status, string code, string message) =>
        Results.Json(new { code, message }, Json, statusCode: status);
}
