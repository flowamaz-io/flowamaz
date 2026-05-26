namespace Flowamaz.Core.Enums;

/// <summary>
/// Audience for a Workflow Interpreter narrative (FUNCTIONAL.md §8.10). CEO = brief business prose
/// (uses F5 AI); Auditor = formal compliance log (deterministic); Developer = technical trace
/// (deterministic).
/// </summary>
public enum NarrativeAudience
{
    Ceo,
    Auditor,
    Developer,
}
