namespace Flowamaz.Core.Models;

public sealed record WorkflowDna(
    Guid WorkflowDefinitionId,
    string TriggerType,
    IReadOnlyList<string> NodeTypes,
    IReadOnlyList<string> ConnectorIds,
    bool HasAiNodes,
    bool HasHumanGates,
    bool HasParallelBranches,
    int NodeCount,
    int EdgeCount,
    long? SlaThresholdMs,
    decimal SlaComplianceRate,
    string DnaHash);

public sealed record WorkflowSimilarity(
    Guid WorkflowDefinitionId,
    string Name,
    int SimilarityScore,
    IReadOnlyList<string> SharedConnectors);

public sealed record CloneSuggestion(
    Guid WorkflowDefinitionId,
    string Name,
    int SimilarityScore);
