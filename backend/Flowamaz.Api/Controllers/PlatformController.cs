using Flowamaz.Api.Authorization;
using Flowamaz.Application.Platform.Services;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Screens for Flowamaz staff (see <c>Platform:StaffOrgIds</c>). Today: the public API limit agreed with an
/// organisation. Customers see their limit on the billing page and can't change it.
/// </summary>
[ApiController]
[Route("api/v1/platform")]
[RequirePlatformStaff]
public sealed class PlatformController : ControllerBase
{
    private readonly PlatformApiLimitService _limits;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<SetApiLimitRequest> _validator;

    public PlatformController(PlatformApiLimitService limits, ICurrentUserService currentUser, IValidator<SetApiLimitRequest> validator)
    {
        _limits = limits;
        _currentUser = currentUser;
        _validator = validator;
    }

    /// <summary>Organisations whose name or slug contains <paramref name="search"/> (at most 50), with their API limits.</summary>
    [HttpGet("organisations")]
    public async Task<ActionResult<List<PlatformOrgApiLimitDto>>> Organisations([FromQuery] string? search, CancellationToken ct)
    {
        if (search is { Length: > 200 }) search = search[..200];
        return Ok(await _limits.SearchAsync(search, ct));
    }

    /// <summary>
    /// Sets the organisation's agreed public API limit in calls a minute (0 = no per-minute limit), or with
    /// <c>perMinute: null</c> returns it to the plan's limit.
    /// </summary>
    [HttpPut("organisations/{id:guid}/api-limit")]
    public async Task<ActionResult<PlatformOrgApiLimitDto>> SetApiLimit(Guid id, [FromBody] SetApiLimitRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(new
            {
                success = false,
                statusCode = 400,
                code = "VALIDATION_ERROR",
                message = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()),
            });

        var result = await _limits.SetAsync(id, request.PerMinute, _currentUser.UserId ?? Guid.Empty, ct);
        if (result is null)
            return NotFound(new
            {
                success = false,
                statusCode = 404,
                code = "ORG_NOT_FOUND",
                message = "No organisation with that id exists. Search for it again by name or slug.",
            });
        return Ok(result);
    }
}

/// <summary>The agreed limit in calls a minute; null returns the organisation to its plan's limit.</summary>
public sealed record SetApiLimitRequest(int? PerMinute);
