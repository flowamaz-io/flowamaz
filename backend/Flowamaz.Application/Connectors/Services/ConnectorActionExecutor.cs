using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>Connector reference parsed from an action node's config.</summary>
public sealed record ConnectorNodeConfig(string ConnectorId, string OperationId, string? CredentialRef, JsonElement Inputs)
{
    /// <summary>
    /// Reads the canonical spec keys (<c>connector_id</c>, <c>operation</c>, <c>inputs</c>,
    /// <c>credential</c>) and the legacy canvas keys (<c>connector</c>, <c>action</c>, <c>fields</c>).
    /// Returns null when the node is not a connector action (e.g. a raw HTTP node with <c>url</c>).
    /// </summary>
    public static ConnectorNodeConfig? TryParse(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object) return null;
        var connector = Str(config, "connector_id") ?? Str(config, "connector");
        if (string.IsNullOrWhiteSpace(connector)) return null;

        var operation = Str(config, "operation") ?? Str(config, "action") ?? string.Empty;
        var inputs = config.TryGetProperty("inputs", out var i) && i.ValueKind == JsonValueKind.Object ? i
            : config.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object ? f
            : JsonDocument.Parse("{}").RootElement;
        return new ConnectorNodeConfig(connector.Trim(), operation.Trim(), Str(config, "credential"), inputs.Clone());
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Runs a connector operation for an action node: resolves the handler (official code handler, or
/// the manifest sandbox for marketplace connectors), interpolates inputs against the instance
/// variables, validates required inputs, resolves the workspace credential from the vault, and maps
/// the outcome to a <see cref="NodeExecutionResult"/> (transient failures retryable, others fatal).
/// Retries are left to the orchestrator's durable RetryPolicy, not looped in-process.
/// </summary>
public sealed class ConnectorActionExecutor
{
    private const int DefaultTimeoutSeconds = 60;

    private readonly IConnectorOperationHandlerRegistry _registry;
    private readonly IConnectorCredentialResolver _credentials;
    private readonly IVariableEvaluationService _variables;
    private readonly ConnectorCallContext _callContext;
    private readonly IConnectorSandbox? _sandbox;
    private readonly ConnectorDispatcher? _dispatcher;
    private readonly ILogger<ConnectorActionExecutor> _logger;

    public ConnectorActionExecutor(
        IConnectorOperationHandlerRegistry registry,
        IConnectorCredentialResolver credentials,
        IVariableEvaluationService variables,
        ConnectorCallContext callContext,
        ILogger<ConnectorActionExecutor> logger,
        IConnectorSandbox? sandbox = null,
        ConnectorDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _credentials = credentials;
        _variables = variables;
        _callContext = callContext;
        _logger = logger;
        _sandbox = sandbox;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context, ConnectorNodeConfig node, CancellationToken cancellationToken)
    {
        var op = $"{node.ConnectorId}/{node.OperationId}";
        _logger.LogInformation("ConnectorActionExecutor.ExecuteAsync enter instance={InstanceId} node={NodeId} op={Op}",
            context.InstanceId, context.Node.Id, op);

        try
        {
            if (string.IsNullOrWhiteSpace(node.OperationId))
                return NodeExecutionResult.Fatal(
                    $"Action node '{context.Node.Id}' uses the {node.ConnectorId} connector but no operation is selected. Open the node config and choose an action.");

            // In-process handler, or (when the sandbox runner is configured) the runner executes it.
            var handler = _registry.Get(node.ConnectorId, node.OperationId);
            var executable = handler is not null || _dispatcher?.CanExecute(node.ConnectorId, node.OperationId) == true;
            var spec = OfficialConnectorCatalog.Find(node.ConnectorId, node.OperationId);
            var connectorSpec = OfficialConnectorCatalog.FindConnector(node.ConnectorId);

            if (!executable && connectorSpec is not null)
            {
                var available = string.Join(", ", connectorSpec.Operations.Where(o => !o.Internal).Select(o => o.Id));
                return NodeExecutionResult.Fatal(
                    $"The {node.ConnectorId} connector has no operation '{node.OperationId}'. Available: {available}.");
            }

            using var input = await BuildInputAsync(context.InstanceId, node.Inputs, cancellationToken);

            if (spec is not null)
            {
                var missing = spec.Required.Where(r => !HasValue(input.RootElement, r)).ToList();
                if (missing.Count > 0)
                    return NodeExecutionResult.Fatal(
                        $"Action node '{context.Node.Id}' ({op}) is missing required input(s): {string.Join(", ", missing)}. " +
                        "Set them in the node config (values can reference variables like {{ variables.amount }}).");
            }

            var credential = await _credentials.ResolveAsync(context.WorkspaceId, node.ConnectorId, node.CredentialRef, cancellationToken);
            if (credential is null && spec?.RequiresCredential == true)
                return NodeExecutionResult.Fatal(
                    $"Action node '{context.Node.Id}' needs a {node.ConnectorId} credential, and none is connected to this workspace. " +
                    "Connect one in Settings → Credentials, then select it in the node config.");

            _callContext.WorkspaceId = context.WorkspaceId;
            _callContext.InstanceId = context.InstanceId;
            _callContext.NodeId = context.Node.Id;

            var timeout = TimeSpan.FromSeconds(context.Node.TimeoutPolicy?.TimeoutSeconds ?? DefaultTimeoutSeconds);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            JsonDocument output;
            try
            {
                if (executable && _dispatcher is not null)
                {
                    output = await _dispatcher.ExecuteAsync(node.ConnectorId, node.OperationId, input, credential,
                        context.WorkspaceId, context.InstanceId, context.Node.Id, timeout, timeoutCts.Token);
                }
                else if (handler is not null)
                {
                    output = await handler.ExecuteAsync(input, credential, timeoutCts.Token);
                }
                else if (_sandbox is not null)
                {
                    var result = await _sandbox.ExecuteAsync(context.WorkspaceId, node.ConnectorId, node.OperationId, input,
                        credential?.Id ?? Guid.Empty, timeoutCts.Token);
                    if (!result.Success)
                        return NodeExecutionResult.Fatal($"{op}: {result.ErrorMessage}");
                    output = result.Output ?? JsonDocument.Parse("{}");
                }
                else
                {
                    return NodeExecutionResult.Fatal($"No executor is available for connector '{node.ConnectorId}'. Install it from the Library.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return NodeExecutionResult.Retryable($"{op} did not finish within {timeout.TotalSeconds:0}s.");
            }

            _logger.LogInformation("ConnectorActionExecutor.ExecuteAsync exit instance={InstanceId} node={NodeId} op={Op}",
                context.InstanceId, context.Node.Id, op);
            return NodeExecutionResult.Ok(output);
        }
        catch (ConnectorOperationException ex)
        {
            _logger.LogWarning("ConnectorActionExecutor: {Op} failed retryable={Retryable}: {Message}", op, ex.Retryable, ex.Message);
            return ex.Retryable ? NodeExecutionResult.Retryable(ex.Message) : NodeExecutionResult.Fatal(ex.Message);
        }
        catch (ConnectorCredentialException ex)
        {
            _logger.LogWarning("ConnectorActionExecutor: {Op} credential problem: {Message}", op, ex.Message);
            return NodeExecutionResult.Fatal(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ConnectorActionExecutor.ExecuteAsync error instance={InstanceId} node={NodeId} op={Op}",
                context.InstanceId, context.Node.Id, op);
            return NodeExecutionResult.Retryable($"{op} failed unexpectedly: {ex.Message}");
        }
    }

    /// <summary>Interpolates every string in the inputs (recursively) against the instance variables.</summary>
    private async Task<JsonDocument> BuildInputAsync(Guid instanceId, JsonElement inputs, CancellationToken ct)
    {
        var resolved = await ResolveAsync(instanceId, inputs, ct);
        return JsonDocument.Parse(JsonSerializer.Serialize(resolved));
    }

    private async Task<object?> ResolveAsync(Guid instanceId, JsonElement element, CancellationToken ct)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var p in element.EnumerateObject()) obj[p.Name] = await ResolveAsync(instanceId, p.Value, ct);
                return obj;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray()) list.Add(await ResolveAsync(instanceId, item, ct));
                return list;
            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                return text.Contains("{{", StringComparison.Ordinal)
                    ? await _variables.InterpolateValueAsync(instanceId, text, ct)
                    : text;
            default:
                return element.Clone();
        }
    }

    private static bool HasValue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(v.GetString()),
            _ => true,
        };
}
