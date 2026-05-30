using Flowamaz.Api.Controllers;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Api.Validators;

/// <summary>
/// Validates a Stripe Checkout request. The redirect URLs are constrained to the platform host
/// (or localhost in Development) so checkout cannot be used to bounce users to an attacker site.
/// </summary>
public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var baseUrl = configuration["Platform:BaseUrl"];
        var isDev = environment.IsDevelopment();

        RuleFor(x => x.PlanId)
            .NotEmpty()
            .WithMessage("A plan must be selected. Choose a plan from the pricing page before checking out.");

        RuleFor(x => x.SuccessUrl)
            .NotEmpty()
            .WithMessage("A success URL is required so we can return you to the app after payment.")
            .Must(url => RedirectUrlPolicy.IsAllowed(url, baseUrl, isDev))
            .WithMessage("The success URL must point back to this application. Use a URL on the platform domain.");

        RuleFor(x => x.CancelUrl)
            .NotEmpty()
            .WithMessage("A cancel URL is required so we can return you to the app if you cancel.")
            .Must(url => RedirectUrlPolicy.IsAllowed(url, baseUrl, isDev))
            .WithMessage("The cancel URL must point back to this application. Use a URL on the platform domain.");
    }
}

/// <summary>
/// Validates a Stripe Customer Portal request. The return URL is constrained to the platform host
/// (or localhost in Development) for the same reason as Checkout.
/// </summary>
public sealed class PortalRequestValidator : AbstractValidator<PortalRequest>
{
    public PortalRequestValidator(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var baseUrl = configuration["Platform:BaseUrl"];
        var isDev = environment.IsDevelopment();

        RuleFor(x => x.ReturnUrl)
            .NotEmpty()
            .WithMessage("A return URL is required so we can bring you back after the billing portal.")
            .Must(url => RedirectUrlPolicy.IsAllowed(url, baseUrl, isDev))
            .WithMessage("The return URL must point back to this application. Use a URL on the platform domain.");
    }
}
