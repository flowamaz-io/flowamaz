using Flowamaz.Application.Workspace.DTOs;
using FluentValidation;

namespace Flowamaz.Application.Workspace.Validators;

public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256).WithMessage("Enter a workspace name.");
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(128)
            .Matches("^[a-z0-9-]+$").WithMessage("Workspace URL may contain only lowercase letters, numbers and hyphens.");
    }
}

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Role).IsInEnum().WithMessage("Choose a valid workspace role.");
    }
}

public sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128).WithMessage("Enter a name for this API key.");
        RuleFor(x => x.EnvironmentId).NotEmpty().WithMessage("Choose an environment for this key.");
        RuleFor(x => x.Scopes).NotEmpty().WithMessage("Select at least one scope.");
    }
}
