using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Human-approval gate portal. Operators list pending gates, view a gate, and decide
/// (approve resumes the instance, reject fails the gate node). All actions are workspace-scoped.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/gates")]
public sealed class WorkflowGatesController : ControllerBase
{
    private readonly GateService _gates;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<GateDecisionRequest> _decisionValidator;

    public WorkflowGatesController(
        GateService gates,
        ICurrentUserService currentUser,
        IValidator<GateDecisionRequest> decisionValidator)
    {
        _gates = gates;
        _currentUser = currentUser;
        _decisionValidator = decisionValidator;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<IReadOnlyList<GateResponse>>> ListPending(Guid workspaceId, CancellationToken cancellationToken) =>
        Ok(await _gates.ListPendingAsync(workspaceId, cancellationToken));

    [HttpGet("{instanceId:guid}/{nodeId}")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<GateResponse>> Get(Guid workspaceId, Guid instanceId, string nodeId, CancellationToken cancellationToken)
    {
        var gate = await _gates.GetAsync(workspaceId, instanceId, nodeId, cancellationToken);
        return gate is null ? NotFound() : Ok(gate);
    }

    [HttpPost("{instanceId:guid}/{nodeId}/decide")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<GateResponse>> Decide(
        Guid workspaceId, Guid instanceId, string nodeId, [FromBody] GateDecisionRequest request, CancellationToken cancellationToken)
    {
        await _decisionValidator.ValidateAndThrowAsync(request, cancellationToken);
        var gate = await _gates.DecideAsync(
            workspaceId, instanceId, nodeId, request.Decision, request.Note, _currentUser.UserId!.Value, cancellationToken);
        return gate is null ? NotFound() : Ok(gate);
    }
}
