using Flowamaz.Application.Auth.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.");
        RuleFor(x => x.OrgSlug).NotEmpty().WithMessage("Enter your organisation URL.");
    }
}
