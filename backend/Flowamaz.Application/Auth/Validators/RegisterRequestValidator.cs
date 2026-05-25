using Flowamaz.Application.Auth.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.OrgName).NotEmpty().MaximumLength(256).WithMessage("Enter your organisation name.");
        RuleFor(x => x.OrgSlug).NotEmpty().MaximumLength(128)
            .Matches("^[a-z0-9-]+$").WithMessage("Organisation URL may contain only lowercase letters, numbers and hyphens.");
        RuleFor(x => x.BillingEmail).NotEmpty().EmailAddress().WithMessage("Enter a valid billing email address.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256).WithMessage("Enter your name.");
        RuleFor(x => x.Password)
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");
        RuleFor(x => x.PlanSlug).NotEmpty().WithMessage("Choose a plan.");
    }
}
