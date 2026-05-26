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
    public static NodeExecutionResult Ok(JsonDocument? output) => new(true, output, null, false);
    public static NodeExecutionResult Retryable(string error) => new(false, null, error, true);
    public static NodeExecutionResult Fatal(string error) => new(false, null, error, false);
}
