using Flowamaz.Core.Workflow;

namespace Flowamaz.Core.Models;

/// <summary>
/// The six-section NL template (FUNCTIONAL.md §6.1). Trigger, rules and systems are optional — nullable so
/// MVC's implicit [Required] on non-nullable strings does not reject a blank section (the clarification
/// step exists precisely to ask about those gaps).
/// </summary>
public sealed record NlWorkflowRequest(
    string WorkflowName,
    string Purpose,
    string? TriggerDescription,
    string StepsDescription,
    string? RulesAndConstraints,
    string? SystemsAndAi,
    string? ExistingContext = null,
    IReadOnlyList<ClarificationAnswer>? Clarifications = null);

public sealed record GenerationResult(
    string YamlContent,
    ValidationResult ValidationResult,
    int TokensUsed,
    bool Cached);

public sealed record PatternMatchResult(
    string PatternName,
    string YamlPatch,
    string Description);
