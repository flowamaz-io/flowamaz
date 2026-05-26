using Flowamaz.Core.Workflow;

namespace Flowamaz.Core.Models;

public sealed record NlWorkflowRequest(
    string WorkflowName,
    string Purpose,
    string TriggerDescription,
    string StepsDescription,
    string RulesAndConstraints,
    string SystemsAndAi,
    string? ExistingContext = null);

public sealed record GenerationResult(
    string YamlContent,
    ValidationResult ValidationResult,
    int TokensUsed,
    bool Cached);

public sealed record PatternMatchResult(
    string PatternName,
    string YamlPatch,
    string Description);
