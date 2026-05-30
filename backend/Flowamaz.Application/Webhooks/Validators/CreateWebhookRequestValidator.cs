using Flowamaz.Application.Webhooks.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Webhooks.Validators;

public sealed class CreateWebhookRequestValidator : AbstractValidator<CreateWebhookRequest>
{
    public CreateWebhookRequestValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId)
            .NotEmpty()
            .WithMessage("A workflow must be selected. Pick a published workflow for this webhook to trigger.");

        RuleFor(x => x.Description)
            .MaximumLength(255)
            .WithMessage("Description is too long. Keep it under 255 characters.");
    }
}
