using Flowamaz.Api.Controllers.Public;
using Flowamaz.Core.Entities.Workflow;
using FluentValidation;

namespace Flowamaz.Api.Validators;

/// <summary>
/// Validates a public-API gate decision. A misspelt decision must not be read as a rejection, and
/// <c>decided_by</c> / <c>note</c> are caller text that ends up in the audit log and run timeline.
/// </summary>
public sealed class PublicGateDecisionRequestValidator : AbstractValidator<PublicGateDecisionRequest>
{
    private static readonly string[] Allowed = ["approved", "rejected"];

    public PublicGateDecisionRequestValidator()
    {
        RuleFor(x => x.Decision)
            .NotEmpty()
            .WithMessage("'decision' is required. Send \"approved\" or \"rejected\".")
            .Must(d => d is not null && Allowed.Contains(d.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"'decision' must be \"approved\" or \"rejected\", not \"{x.Decision}\". Fix the value and send the decision again.");

        RuleFor(x => x.Note)
            .MaximumLength(GateDecisionLimits.NoteMaxLength)
            .WithMessage($"'note' is too long. Keep it to {GateDecisionLimits.NoteMaxLength} characters or fewer.");

        RuleFor(x => x.DecidedBy)
            .MaximumLength(GateDecisionLimits.DecidedByMaxLength)
            .WithMessage($"'decided_by' is too long. Send the person's name (and role) in {GateDecisionLimits.DecidedByMaxLength} characters or fewer.")
            .Must(v => v is null || !v.Any(char.IsControl))
            .WithMessage("'decided_by' contains control characters (such as line breaks). Send the name as a single line of text.");
    }
}
