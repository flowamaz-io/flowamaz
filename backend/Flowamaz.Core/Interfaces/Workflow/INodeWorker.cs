using System.Text.Json;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Executes a single node of a given <see cref="NodeType"/>. Implementations are registered in the
/// <c>NodeWorkerRegistry</c> and selected by the worker loop for the node it is about to run.
/// </summary>
public interface INodeWorker
{
    NodeType SupportedType { get; }

    Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Everything a node worker needs to run one node of one instance.</summary>
public sealed record NodeExecutionContext(
    Guid InstanceId,
    Guid WorkspaceId,
    SfgNode Node,
    WorkflowGraph Graph,
    IReadOnlyDictionary<string, JsonElement> Variables,
    string LeaseId,
    /// <summary>Variable names whose values must be redacted before any AI call. Null = no redaction.</summary>
    IReadOnlySet<string>? SensitiveVariableNames = null);

/// <summary>
/// Outcome of running a node. <see cref="ShouldRetry"/> is the worker's verdict on whether a
/// failure is transient — the orchestrator still gates actual retries on the node's RetryPolicy.
/// </summary>
public sealed record NodeExecutionResult(
    bool Success,
    JsonDocument? Output,
    string? ErrorMessage,
    bool ShouldRetry)
{
    private const string GateWaitingPrefix = "GATE_WAITING:";

    public static NodeExecutionResult Ok(JsonDocument? output) => new(true, output, null, false);
    public static NodeExecutionResult Retryable(string error) => new(false, null, error, true);
    public static NodeExecutionResult Fatal(string error) => new(false, null, error, false);

    /// <summary>
    /// Signals that a HumanGate node has paused the instance pending an approval decision.
    /// The orchestrator detects <see cref="IsGateWaiting"/> and parks the instance rather than
    /// treating this as a failure.
    /// </summary>
    public static NodeExecutionResult GateWaiting(string gateDecisionId) =>
        new(false, null, $"{GateWaitingPrefix}{gateDecisionId}", false);

    /// <summary>True when this result represents a parked gate rather than a real failure.</summary>
    public bool IsGateWaiting => ErrorMessage?.StartsWith(GateWaitingPrefix, StringComparison.Ordinal) == true;

    /// <summary>Extracts the gate decision ID when <see cref="IsGateWaiting"/> is true.</summary>
    public string? GateDecisionId => IsGateWaiting
        ? ErrorMessage![GateWaitingPrefix.Length..]
        : null;
}
