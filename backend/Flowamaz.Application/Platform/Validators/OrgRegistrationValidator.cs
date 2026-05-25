using Flowamaz.Application.Platform.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Platform.Validators;

public sealed class OrgRegistrationValidator : AbstractValidator<OrgRegistrationRequest>
{
    private static readonly string[] KnownPlanSlugs = ["community", "starter", "pro", "enterprise"];

    public OrgRegistrationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);

        RuleFor(x => x.Slug)
            .NotEmpty().MaximumLength(128)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase letters and numbers separated by single hyphens (e.g. 'acme-finance').");

        RuleFor(x => x.BillingEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(256);

        RuleFor(x => x.OwnerPassword)
            .NotEmpty().MinimumLength(8).MaximumLength(128)
            .WithMessage("Password must be between 8 and 128 characters.");

        RuleFor(x => x.PlanSlug)
            .NotEmpty()
            .Must(slug => KnownPlanSlugs.Contains(slug))
            .WithMessage("Plan must be one of: community, starter, pro, enterprise.");

        RuleFor(x => x.DataRegion).IsInEnum();
    }
}
