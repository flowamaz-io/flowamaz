using Flowamaz.Application.Workflow.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Workflow.Validators;

public sealed class CreateWorkflowDefinitionRequestValidator : AbstractValidator<CreateWorkflowDefinitionRequest>
{
    public CreateWorkflowDefinitionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256).WithMessage("Enter a workflow name.");
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(128)
            .Matches("^[a-z0-9-]+$").WithMessage("Workflow slug may contain only lowercase letters, numbers and hyphens.");
        RuleFor(x => x.YamlContent).NotEmpty().WithMessage("Provide the workflow YAML.");
        RuleFor(x => x.CreatedByMethod).IsInEnum().WithMessage("Choose how this workflow was created.");
    }
}

public sealed class UpdateWorkflowDefinitionRequestValidator : AbstractValidator<UpdateWorkflowDefinitionRequest>
{
    public UpdateWorkflowDefinitionRequestValidator()
    {
        RuleFor(x => x.YamlContent).NotEmpty().WithMessage("Provide the workflow YAML to save.");
    }
}

public sealed class TriggerInstanceRequestValidator : AbstractValidator<TriggerInstanceRequest>
{
    public TriggerInstanceRequestValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty().WithMessage("A workflow_definition_id is required to trigger a run.");
    }
}

public sealed class GateDecisionRequestValidator : AbstractValidator<GateDecisionRequest>
{
    private static readonly string[] Allowed = ["approved", "rejected"];

    public GateDecisionRequestValidator()
    {
        RuleFor(x => x.Decision)
            .NotEmpty()
            .Must(d => Allowed.Contains(d, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Decision must be 'approved' or 'rejected'.");
    }
}
