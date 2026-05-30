using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Per-user key/value preferences (JSON values) — e.g. the getting-started checklist state keyed
/// "checklist_{workspaceId}". Scoped to the current user.
/// </summary>
[ApiController]
[Route("api/v1/preferences")]
public sealed class UserPreferencesController : ControllerBase
{
    private readonly IUserPreferenceRepository _repo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public UserPreferencesController(IUserPreferenceRepository repo, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    private Guid UserId => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("Sign in to read your preferences.");

    [HttpGet("{key}")]
    public async Task<ActionResult<PreferenceResponse>> Get(string key, CancellationToken ct)
    {
        var pref = await _repo.GetAsync(UserId, key, ct);
        return Ok(new PreferenceResponse(key, pref?.Value));
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<PreferenceResponse>> Put(string key, [FromBody] PreferenceRequest request, CancellationToken ct)
    {
        var pref = await _repo.GetAsync(UserId, key, ct);
        if (pref is null)
        {
            pref = new UserPreference { UserId = UserId, Key = key, Value = request.Value };
            await _repo.AddAsync(pref, ct);
        }
        else
        {
            pref.Value = request.Value;
            _repo.Update(pref);
        }
        await _unitOfWork.SaveChangesAsync(ct);
        return Ok(new PreferenceResponse(key, pref.Value));
    }
}

public sealed record PreferenceRequest(string Value);
public sealed record PreferenceResponse(string Key, string? Value);
