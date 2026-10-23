using Flowamaz.Api.Controllers;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Api.Validators;

/// <summary>Stripe Connect onboarding return/refresh URLs must point back to this application.</summary>
public sealed class PublisherOnboardingRequestValidator : AbstractValidator<PublisherOnboardingRequest>
{
    public PublisherOnboardingRequestValidator(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var baseUrl = configuration["Platform:BaseUrl"];
        var isDev = environment.IsDevelopment();
        RuleFor(x => x.ReturnUrl).NotEmpty().Must(u => RedirectUrlPolicy.IsAllowed(u, baseUrl, isDev))
            .WithMessage("The return URL must point back to this application (Settings → Publisher).");
        RuleFor(x => x.RefreshUrl).NotEmpty().Must(u => RedirectUrlPolicy.IsAllowed(u, baseUrl, isDev))
            .WithMessage("The refresh URL must point back to this application (Settings → Publisher).");
    }
}

/// <summary>Checkout success/cancel URLs for a paid workflow must point back to this application.</summary>
public sealed class TemplatePurchaseRequestValidator : AbstractValidator<TemplatePurchaseRequest>
{
    public TemplatePurchaseRequestValidator(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var baseUrl = configuration["Platform:BaseUrl"];
        var isDev = environment.IsDevelopment();
        RuleFor(x => x.SuccessUrl).NotEmpty().Must(u => RedirectUrlPolicy.IsAllowed(u, baseUrl, isDev))
            .WithMessage("The success URL must point back to this application. Use the template's page URL.");
        RuleFor(x => x.CancelUrl).NotEmpty().Must(u => RedirectUrlPolicy.IsAllowed(u, baseUrl, isDev))
            .WithMessage("The cancel URL must point back to this application. Use the template's page URL.");
    }
}
