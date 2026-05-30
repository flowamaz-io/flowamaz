using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Records "was this article helpful?" feedback from the help panel. Logged as structured telemetry
/// (article slug + helpful flag + user) for content prioritisation.
/// </summary>
[ApiController]
[Route("api/v1/help")]
[AllowAnonymous]
public sealed class HelpFeedbackController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<HelpFeedbackController> _logger;

    public HelpFeedbackController(ICurrentUserService currentUser, ILogger<HelpFeedbackController> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("feedback")]
    public IActionResult Feedback([FromBody] HelpFeedbackRequest request)
    {
        _logger.LogInformation(
            "HelpFeedback slug={Slug} helpful={Helpful} user={UserId}",
            request.Slug, request.Helpful, _currentUser.UserId);
        return Ok(new { recorded = true });
    }
}

public sealed record HelpFeedbackRequest(string Slug, bool Helpful);
