using Flowamaz.Api.Controllers;
using FluentValidation;

namespace Flowamaz.Api.Validators;

/// <summary>
/// Validates a request to publish a workspace workflow into the template gallery. Category is
/// constrained to the known gallery taxonomy so the gallery filters stay coherent.
/// </summary>
public sealed class PublishTemplateRequestValidator : AbstractValidator<PublishTemplateRequest>
{
    // Matches WorkflowTemplate.Category taxonomy: Finance | HR | IT | Legal | Operations | Custom.
    private static readonly string[] ValidCategories =
        ["Finance", "HR", "IT", "Legal", "Operations", "Custom"];

    public PublishTemplateRequestValidator()
    {
        RuleFor(x => x.WorkflowId)
            .NotEmpty()
            .WithMessage("Select the workflow to publish as a template.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A template name is required so others can find it in the gallery.")
            .MaximumLength(100).WithMessage("Template names must be 100 characters or fewer.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A description is required so others know what this template does.")
            .MaximumLength(500).WithMessage("Template descriptions must be 500 characters or fewer.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("A category is required so the template lands in the right gallery section.")
            .Must(c => ValidCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Choose a category from: {string.Join(", ", ValidCategories)}.");
    }
}

/// <summary>Validates a request to install a template into a workspace as a new workflow.</summary>
public sealed class InstallTemplateRequestValidator : AbstractValidator<InstallTemplateRequest>
{
    public InstallTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Give the new workflow a name so you can find it later.")
            .MaximumLength(200).WithMessage("Workflow names must be 200 characters or fewer.");
    }
}
