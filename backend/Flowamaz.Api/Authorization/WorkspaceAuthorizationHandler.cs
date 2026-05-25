using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flowamaz.Api.Authorization;

/// <summary>
/// Authorization filter behind <see cref="RequireWorkspaceRoleAttribute"/>. Resolves the workspace
/// id from the route ("workspaceId" or "id"), confirms the workspace belongs to the caller's org
/// (returns 404 otherwise — never reveals another org's workspace), then enforces the minimum role
/// via <see cref="IWorkspaceAuthorizationService"/>. API-key callers cannot perform role-gated
/// management actions in Phase 1.
/// </summary>
public sealed class WorkspaceAuthorizationHandler : IAsyncAuthorizationFilter
{
    private readonly ICurrentUserService _currentUser;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWorkspaceAuthorizationService _authorization;
    private readonly WorkspaceRole _minimumRole;

    public WorkspaceAuthorizationHandler(
        ICurrentUserService currentUser,
        IWorkspaceService workspaceService,
        IWorkspaceAuthorizationService authorization,
        WorkspaceRole minimumRole)
    {
        _currentUser = currentUser;
        _workspaceService = workspaceService;
        _authorization = authorization;
        _minimumRole = minimumRole;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!TryResolveWorkspaceId(context, out var workspaceId))
        {
            context.Result = Envelope(context, 400, "WORKSPACE_ID_MISSING", "A workspace id is required for this request.");
            return;
        }

        if (!_currentUser.IsAuthenticated)
        {
            context.Result = Envelope(context, 401, "AUTH_REQUIRED", "Authentication is required. Sign in and try again.");
            return;
        }

        if (_currentUser.IsApiKey)
        {
            context.Result = _currentUser.ApiKeyWorkspaceId == workspaceId
                ? Envelope(context, 403, "API_KEY_NOT_PERMITTED", "API keys cannot perform workspace management actions.")
                : NotFound(context);
            return;
        }

        var orgId = _currentUser.OrgId ?? Guid.Empty;
        var workspace = await _workspaceService.GetByIdAsync(workspaceId, orgId);
        if (workspace is null)
        {
            // 404 (not 403) so we never reveal that a workspace in another org exists.
            context.Result = NotFound(context);
            return;
        }

        try
        {
            await _authorization.RequireMinimumRoleAsync(workspaceId, _currentUser.UserId!.Value, _minimumRole);
        }
        catch (InsufficientRoleException)
        {
            context.Result = Envelope(context, 403, "WORKSPACE_INSUFFICIENT_ROLE",
                $"Insufficient permissions. Required role: {_minimumRole} or higher.");
        }
    }

    private static bool TryResolveWorkspaceId(AuthorizationFilterContext context, out Guid workspaceId)
    {
        var route = context.RouteData.Values;
        var raw = route.TryGetValue("workspaceId", out var ws) ? ws?.ToString()
            : route.TryGetValue("id", out var id) ? id?.ToString()
            : null;
        return Guid.TryParse(raw, out workspaceId);
    }

    private static ObjectResult NotFound(AuthorizationFilterContext context) =>
        Envelope(context, 404, "WORKSPACE_NOT_FOUND", "Workspace not found. Check the id, or your access to it.");

    private static ObjectResult Envelope(AuthorizationFilterContext context, int status, string code, string message) =>
        new(new
        {
            success = false,
            statusCode = status,
            code,
            message,
            correlationId = context.HttpContext.Items.TryGetValue("CorrelationId", out var v) && v is string s ? s : "",
        })
        { StatusCode = status };
}
