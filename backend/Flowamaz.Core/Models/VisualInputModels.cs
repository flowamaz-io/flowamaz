namespace Flowamaz.Core.Models;

public sealed record VisualInputResult(
    string YamlDraft,
    IReadOnlyList<LowConfidenceElement> LowConfidenceElements,
    decimal CostUsd,
    int TokensUsed);

public sealed record LowConfidenceElement(
    string Id,
    string Label,
    string DetectedType,
    double Confidence,
    IReadOnlyList<string> Annotations);

public sealed record ConversationImportResult(
    string YamlContent,
    ExtractedProcess ExtractedProcess,
    double ConfidenceScore,
    int TokensUsed);

public sealed record ExtractedProcess(
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Approvers,
    IReadOnlyList<string> Systems,
    NlWorkflowRequest GeneratedRequest);
