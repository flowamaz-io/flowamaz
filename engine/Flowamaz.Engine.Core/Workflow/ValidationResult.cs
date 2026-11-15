namespace Flowamaz.Core.Workflow;

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings,
    IReadOnlyList<ValidationIssue> Info)
{
    public static ValidationResult Valid() =>
        new(true, [], [], []);

    public static ValidationResult WithIssues(
        IReadOnlyList<ValidationIssue> errors,
        IReadOnlyList<ValidationIssue> warnings,
        IReadOnlyList<ValidationIssue> info) =>
        new(errors.Count == 0, errors, warnings, info);
}

public sealed record ValidationIssue(
    int Layer,
    string Code,
    string Message,
    string? NodeId,
    int? Line,
    int? Column);
