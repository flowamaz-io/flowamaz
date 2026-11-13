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

/// <summary>The extraction a user reviewed in the conversation step (POST /workflows/create, method conversation).</summary>
public sealed class ConversationExtractionValidator : AbstractValidator<ConversationExtraction>
{
    public const int MaxSteps = ConversationImportService.MaxSteps;
    private const int MaxText = 500;

    public ConversationExtractionValidator()
    {
        RuleFor(x => x.WorkflowName).MaximumLength(200)
            .WithMessage("Workflow name is too long. Keep it under 200 characters.");
        RuleFor(x => x.Summary).MaximumLength(2000).WithMessage("The summary is too long. Keep it under 2,000 characters.");
        RuleFor(x => x.Trigger).MaximumLength(2000).WithMessage("The trigger is too long. Keep it under 2,000 characters.");

        RuleFor(x => x.Steps)
            .NotNull().WithMessage("Keep at least one step before generating the workflow.")
            .Must(s => s is not null && s.Count > 0).WithMessage("Every step was removed. Keep or add at least one step, or describe the process in plain English instead.")
            .Must(s => s is null || s.Count != 1).WithMessage("A workflow needs at least two steps. Add the step before or after this one (who does what next).")
            .Must(s => s is null || s.Count <= MaxSteps).WithMessage($"More than {MaxSteps} steps. Split the process into smaller workflows.");
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.Title).NotEmpty().WithMessage("A step has no text. Describe it, or remove it.")
                .MaximumLength(MaxText).WithMessage($"A step is too long. Keep each under {MaxText} characters.");
            step.RuleFor(s => s.Owner).MaximumLength(MaxText).WithMessage($"A step owner is too long. Keep it under {MaxText} characters.");
            step.RuleFor(s => s.System).MaximumLength(MaxText).WithMessage($"A step's system is too long. Keep it under {MaxText} characters.");
        });

        RuleFor(x => x.Decisions).Must(d => d is null || d.Count <= ConversationImportService.MaxDecisions)
            .WithMessage($"More than {ConversationImportService.MaxDecisions} conditions. Keep the ones that change what happens.");
        RuleForEach(x => x.Decisions).ChildRules(d =>
        {
            d.RuleFor(x => x.Condition).NotEmpty().WithMessage("A condition is empty. Fill it in (e.g. \"amount above $5,000\"), or remove it.")
                .MaximumLength(MaxText).WithMessage($"A condition is too long. Keep it under {MaxText} characters.");
            d.RuleFor(x => x.Outcome).NotEmpty().WithMessage("A condition has no outcome. Say what happens then (e.g. \"CFO approves\"), or remove it.")
                .MaximumLength(MaxText).WithMessage($"A condition's outcome is too long. Keep it under {MaxText} characters.");
        });

        RuleFor(x => x.Approvers).Must(a => a is null || a.Count <= ConversationImportService.MaxApprovers)
            .WithMessage($"More than {ConversationImportService.MaxApprovers} approvers. Group them by role instead.");
        RuleForEach(x => x.Approvers).ChildRules(a =>
        {
            a.RuleFor(x => x.Who).NotEmpty().WithMessage("An approver is empty. Name the person or role, or remove it.")
                .MaximumLength(MaxText).WithMessage($"An approver is too long. Keep it under {MaxText} characters.");
            a.RuleFor(x => x.When).MaximumLength(MaxText).WithMessage($"An approver's condition is too long. Keep it under {MaxText} characters.");
        });

        RuleFor(x => x.Systems).Must(BeShortList).WithMessage($"Keep systems to {ConversationImportService.MaxListItems} entries of under {MaxText} characters.");
        RuleFor(x => x.Agreements).Must(BeShortList).WithMessage($"Keep agreements to {ConversationImportService.MaxListItems} entries of under {MaxText} characters.");
        RuleFor(x => x.OpenQuestions).Must(BeShortList).WithMessage($"Keep open questions to {ConversationImportService.MaxListItems} entries of under {MaxText} characters.");
        RuleFor(x => x.Participants).Must(p => p is null || p.Count <= ConversationImportService.MaxListItems)
            .WithMessage($"More than {ConversationImportService.MaxListItems} participants. Re-run Extract on a shorter thread.");
        RuleFor(x => x.Confidence).InclusiveBetween(0, 1).WithMessage("Confidence must be between 0 and 1. Re-run Extract to get a fresh review.");
    }

    private static bool BeShortList(IReadOnlyList<string>? items) =>
        items is null || (items.Count <= ConversationImportService.MaxListItems && items.All(i => i is not null && i.Length <= MaxText));
}
