namespace Flowamaz.Core.Models;

/// <summary>
/// One element detected in an uploaded diagram (FUNCTIONAL.md §6.3). Low-confidence elements
/// (below <see cref="VisualPreviewResult.ConfidenceThreshold"/>) are flagged with
/// <see cref="ReviewNeeded"/> and carry the vision model's <see cref="Reason"/> so the user can
/// keep, rename or remove them before the workflow is created.
/// </summary>
public sealed record DetectedNode(
    string Id,
    string Label,
    string Type,
    double Confidence,
    bool ReviewNeeded,
    string? Reason,
    IReadOnlyList<string> Annotations,
    IReadOnlyList<string> SuggestedConnectors,
    double? X = null,
    double? Y = null);

/// <summary>A connection between two detected (or confirmed) nodes. Label becomes the edge's <c>via</c>.</summary>
public sealed record DetectedEdge(string From, string To, string? Label = null);

/// <summary>Result of the review step: the full detected graph, nothing dropped.</summary>
public sealed record VisualPreviewResult(
    IReadOnlyList<DetectedNode> Nodes,
    IReadOnlyList<DetectedEdge> Edges,
    double ConfidenceThreshold,
    decimal CostUsd,
    int TokensUsed);

/// <summary>A node the user confirmed in the review step (possibly renamed).</summary>
public sealed record ConfirmedVisualNode(
    string Id,
    string Label,
    string Type,
    IReadOnlyList<string>? SuggestedConnectors = null);

/// <summary>
/// The graph the user confirmed. Removed nodes are simply absent from <see cref="Nodes"/>; edges that
/// still reference them are bridged (predecessor → successor) when the YAML is built.
/// </summary>
public sealed record ConfirmedVisualGraph(
    IReadOnlyList<ConfirmedVisualNode> Nodes,
    IReadOnlyList<DetectedEdge> Edges);
