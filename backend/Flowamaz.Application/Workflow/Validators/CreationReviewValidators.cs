using FluentValidation;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Models;

namespace Flowamaz.Application.Workflow.Validators;

/// <summary>
/// Shape checks for an NL creation request (used by POST /workflows/clarify and the NL path of
/// POST /workflows/create). Content-quality checks (too-brief steps, too complex) stay in
/// NlYamlGenerationService so they keep their existing messages.
/// </summary>
public sealed class NlWorkflowRequestValidator : AbstractValidator<NlWorkflowRequest>
{
    public const int MaxFieldLength = 8000;
    public const int MaxClarifications = 5;
    public const int MaxAnswerLength = 2000;

    public NlWorkflowRequestValidator()
    {
        RuleFor(x => x.WorkflowName)
            .NotEmpty().WithMessage("Enter a workflow name so we know what to call it.")
            .MaximumLength(200).WithMessage("Workflow name is too long. Keep it under 200 characters.");

        RuleFor(x => x.StepsDescription)
            .NotEmpty().WithMessage("Describe the steps of the workflow so we can work out what is missing.")
            .MaximumLength(MaxFieldLength).WithMessage($"Steps description is too long. Keep it under {MaxFieldLength} characters or split the process into sub-workflows.");

        RuleFor(x => x.Purpose).MaximumLength(MaxFieldLength)
            .WithMessage($"Purpose is too long. Keep it under {MaxFieldLength} characters.");
        RuleFor(x => x.TriggerDescription).MaximumLength(MaxFieldLength)
            .WithMessage($"Trigger description is too long. Keep it under {MaxFieldLength} characters.");
        RuleFor(x => x.RulesAndConstraints).MaximumLength(MaxFieldLength)
            .WithMessage($"Rules and constraints are too long. Keep them under {MaxFieldLength} characters.");
        RuleFor(x => x.SystemsAndAi).MaximumLength(MaxFieldLength)
            .WithMessage($"Systems description is too long. Keep it under {MaxFieldLength} characters.");

        RuleFor(x => x.Clarifications)
            .Must(c => c is null || c.Count <= MaxClarifications)
            .WithMessage($"Send at most {MaxClarifications} clarification answers. Remove extra answers and try again.");

        RuleForEach(x => x.Clarifications).ChildRules(a =>
        {
            a.RuleFor(c => c.Question)
                .NotEmpty().WithMessage("Each clarification answer must include the question it answers.")
                .MaximumLength(500).WithMessage("A clarification question is too long. Keep it under 500 characters.");
            a.RuleFor(c => c.Answer)
                .MaximumLength(MaxAnswerLength).WithMessage($"A clarification answer is too long. Keep it under {MaxAnswerLength} characters.");
            a.RuleFor(c => c.Topic)
                .NotEmpty().WithMessage("Each clarification answer must include its topic (trigger, approver, rejection, systems or other).");
        });
    }
}

/// <summary>The graph a user confirmed in the visual review step (POST /workflows/create, method visual).</summary>
public sealed class ConfirmedVisualGraphValidator : AbstractValidator<ConfirmedVisualGraph>
{
    public const int MaxNodes = 100;
    public const int MaxEdges = 300;

    public ConfirmedVisualGraphValidator()
    {
        RuleFor(x => x.Nodes)
            .NotNull().WithMessage("Keep at least one detected step before creating the workflow.")
            .Must(n => n is not null && n.Count > 0).WithMessage("Every detected step was removed. Keep at least one step, or start from the canvas instead.")
            .Must(n => n is null || n.Count <= MaxNodes).WithMessage($"The diagram has more than {MaxNodes} steps. Split it into smaller workflows.");

        RuleFor(x => x.Edges)
            .NotNull().WithMessage("Send the detected connections (edges), even if the list is empty.")
            .Must(e => e is null || e.Count <= MaxEdges).WithMessage($"The diagram has more than {MaxEdges} connections. Split it into smaller workflows.");

        RuleFor(x => x.Nodes)
            .Must(n => n is null || n.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == n.Count)
            .WithMessage("Two steps share the same id. Re-upload the diagram to get a fresh review.");

        RuleForEach(x => x.Nodes).ChildRules(n =>
        {
            n.RuleFor(x => x.Id).NotEmpty().WithMessage("A step is missing its id. Re-upload the diagram to get a fresh review.");
            n.RuleFor(x => x.Label)
                .NotEmpty().WithMessage("Every kept step needs a name. Rename the unnamed step or remove it.")
                .MaximumLength(200).WithMessage("A step name is too long. Keep step names under 200 characters.");
            n.RuleFor(x => x.Type)
                .Must(t => t is not null && VisualInputService.AllowedNodeTypes.Contains(t))
                .WithMessage(x => $"Step '{x.Label}' has an unknown type '{x.Type}'. Use one of: {string.Join(", ", VisualInputService.AllowedNodeTypes)}.");
        });

        RuleForEach(x => x.Edges).ChildRules(e =>
        {
            e.RuleFor(x => x.From).NotEmpty().WithMessage("A connection is missing its source step. Re-upload the diagram to get a fresh review.");
            e.RuleFor(x => x.To).NotEmpty().WithMessage("A connection is missing its target step. Re-upload the diagram to get a fresh review.");
            e.RuleFor(x => x.Label).MaximumLength(500).WithMessage("A connection label is too long. Keep it under 500 characters.");
        });
    }
}
