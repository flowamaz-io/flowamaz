using FluentValidation;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>Library publishers (FUNCTIONAL.md §23.9): public profiles and the publisher portal.</summary>
[ApiController]
[Route("api/v1")]
public sealed class PublishersController : ControllerBase
{
    private readonly IPublisherService _publishers;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<UpdatePublisherProfileRequest> _validator;

    public PublishersController(IPublisherService publishers, ICurrentUserService currentUser, IValidator<UpdatePublisherProfileRequest> validator)
    {
        _publishers = publishers;
        _currentUser = currentUser;
        _validator = validator;
    }

    /// <summary>A publisher's profile and live Library items; any signed-in user can see it.</summary>
    [HttpGet("library/publishers/{publisherId}")]
    [Authorize]
    public async Task<ActionResult<PublisherProfileView>> Profile(string publisherId, CancellationToken ct) =>
        Ok(await _publishers.GetProfileAsync(publisherId, ct));

    /// <summary>The organisation's publisher portal: profile, item stats, submissions.</summary>
    [HttpGet("publisher/portal")]
    [RequireOrgOwner]
    public async Task<ActionResult<PublisherPortal>> Portal(CancellationToken ct) =>
        Ok(await _publishers.GetPortalAsync(RequireOrg(), ct));

    [HttpPut("publisher/profile")]
    [RequireOrgOwner]
    public async Task<ActionResult<PublisherProfileView>> UpdateProfile([FromBody] UpdatePublisherProfileRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        return Ok(await _publishers.UpdateProfileAsync(RequireOrg(), request, _currentUser.UserId!.Value, ct));
    }

    private Guid RequireOrg() =>
        _currentUser.OrgId ?? throw LibraryException.NoOrganisation();
}

/// <summary>Templates published from the app wait here for a Flowamaz staff decision.</summary>
[ApiController]
[Route("api/v1/platform/library/reviews")]
[RequirePlatformStaff]
public sealed class PlatformLibraryReviewController : ControllerBase
{
    private readonly IPublisherService _publishers;
    private readonly ICurrentUserService _currentUser;

    public PlatformLibraryReviewController(IPublisherService publishers, ICurrentUserService currentUser)
    {
        _publishers = publishers;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<LibraryReviewQueue>> Queue(CancellationToken ct) => Ok(await _publishers.GetReviewQueueAsync(ct));

    [HttpPost("templates/{templateId:guid}/approve")]
    public async Task<ActionResult<LibraryReviewTemplate>> Approve(Guid templateId, [FromBody] LibraryReviewDecision? request, CancellationToken ct) =>
        Ok(await _publishers.ApproveTemplateAsync(templateId, request?.Notes, _currentUser.UserId!.Value, ct));

    [HttpPost("templates/{templateId:guid}/reject")]
    public async Task<ActionResult<LibraryReviewTemplate>> Reject(Guid templateId, [FromBody] LibraryReviewDecision request, CancellationToken ct) =>
        Ok(await _publishers.RejectTemplateAsync(templateId, request.Notes ?? "", _currentUser.UserId!.Value, ct));
}

public sealed record LibraryReviewDecision(string? Notes);

public sealed class UpdatePublisherProfileRequestValidator : AbstractValidator<UpdatePublisherProfileRequest>
{
    public UpdatePublisherProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Give the publisher a display name, e.g. your company name.")
            .MaximumLength(100).WithMessage("The display name is longer than 100 characters. Shorten it.");
        RuleFor(x => x.Bio).MaximumLength(600).WithMessage("The description is longer than 600 characters. Shorten it.");
        RuleFor(x => x.WebsiteUrl).MaximumLength(200).WithMessage("The website address is longer than 200 characters.");
        RuleFor(x => x.GithubLogin).MaximumLength(60).WithMessage("The GitHub account name is too long; use the name only, e.g. acme-labs.");
    }
}
