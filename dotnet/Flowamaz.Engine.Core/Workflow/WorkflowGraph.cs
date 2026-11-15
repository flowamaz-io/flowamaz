using System.Text.Json;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Workflow;

/// <summary>
/// A parsed, validated in-memory workflow graph (the SFG). Produced by <see cref="SfgParser"/> from
/// the workflow YAML and walked by the orchestrator. Immutable.
/// </summary>
public sealed record WorkflowGraph(
    string WorkflowId,
    string Version,
    IReadOnlyList<SfgNode> Nodes,
    IReadOnlyList<SfgEdge> Edges,
    WorkflowMetadata Metadata,
    IReadOnlyList<WorkflowInput>? Inputs = null)
{
    /// <summary>The inputs declared under <c>spec.variables</c> (or top-level <c>variables:</c>); empty when none.</summary>
    public IReadOnlyList<WorkflowInput> DeclaredInputs => Inputs ?? [];

    public SfgNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public SfgNode TriggerNode => Nodes.First(n => n.Type == NodeType.Trigger);

    public IEnumerable<SfgEdge> OutgoingEdges(string nodeId) => Edges.Where(e => e.FromNodeId == nodeId);

    public IEnumerable<SfgEdge> IncomingEdges(string nodeId) => Edges.Where(e => e.ToNodeId == nodeId);
}

/// <summary>A single node. <see cref="Config"/> is node-type-specific JSON parsed from the YAML.</summary>
public sealed record SfgNode(
    string Id,
    NodeType Type,
    string Label,
    JsonDocument Config,
    RetryPolicy? RetryPolicy,
    TimeoutPolicy? TimeoutPolicy,
    CompensationBlock? Compensation);

/// <summary>A directed edge. A null <see cref="Condition"/> is unconditional.</summary>
/// <param name="IsDefault">An "else" branch of a router: followed only when no sibling edge's condition holds.</param>
/// <param name="Via">Branch label from YAML (<c>via: approved</c>). Without a matching router branch
/// config it routes on the upstream decision (e.g. a human gate's approved/rejected).</param>
public sealed record SfgEdge(string Id, string FromNodeId, string ToNodeId, string? Condition, bool IsDefault = false, string? Via = null);

public sealed record WorkflowMetadata(string Name, string? Description);

/// <summary>
/// An input the workflow declares (<c>variables: amount: { type: number, required: true }</c>). Type is lower-case
/// (string, number, integer, boolean, object, array) or null when not given.
/// </summary>
public sealed record WorkflowInput(string Name, string? Type, bool Required);

public sealed record RetryPolicy(int MaxAttempts, int BackoffSeconds, double BackoffMultiplier);

public sealed record TimeoutPolicy(int TimeoutSeconds, string? OnTimeoutNodeId);

public sealed record CompensationBlock(string Strategy, string? CompensateNodeId);
