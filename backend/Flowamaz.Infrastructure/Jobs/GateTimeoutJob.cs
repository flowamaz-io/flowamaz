using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 60s) that applies each expired gate's on-timeout action:
/// - escalate: reassign once to EscalateToEmail with a fresh window (the gate stays Pending and
///   decidable — previously it became "Escalated", which nothing could ever decide);
/// - auto-approve / auto-reject: record the decision and resume / fail the run;
/// - fail (default, or escalate with no target / already escalated): fail the gate node.
/// </summary>
[DisallowConcurrentExecution]
public sealed class GateTimeoutJob : IJob
{
    private readonly IGateDecisionRepository _gates;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GateTimeoutJob> _logger;
    private readonly IEmailService? _email;
    private readonly IConfiguration? _config;

    public GateTimeoutJob(
        IGateDecisionRepository gates,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        IUnitOfWork unitOfWork,
        ILogger<GateTimeoutJob> logger,
        IEmailService? email = null,
        IConfiguration? config = null)
    {
        _email = email;
        _config = config;
        _gates = gates;
        _events = events;
        _orchestrator = orchestrator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var expired = await _gates.GetExpiredAsync(DateTime.UtcNow, context.CancellationToken);
            foreach (var gate in expired)
            {
                await ApplyTimeoutAsync(gate, context.CancellationToken);
            }

            if (expired.Count > 0)
            {
                _logger.LogInformation("GateTimeoutJob processed {Count} expired gates", expired.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GateTimeoutJob error while scanning for expired gates");
        }
    }

    private async Task ApplyTimeoutAsync(GateDecision gate, CancellationToken ct)
    {
        var action = GateTimeoutActions.Normalize(gate.OnTimeout);
        var canEscalate = action == GateTimeoutActions.Escalate
                          && !string.IsNullOrWhiteSpace(gate.EscalateToEmail) && gate.EscalationCount == 0;

        if (canEscalate)
        {
            var window = TimeSpan.FromHours(gate.TimeoutHours is > 0 ? gate.TimeoutHours.Value : 48);
            gate.AssignedToEmail = gate.EscalateToEmail;
            gate.ExpiresAt = DateTime.UtcNow.Add(window);
            gate.EscalationCount++;
            _gates.Update(gate);
            await AppendDecisionEventAsync(gate, "GateEscalated",
                $$"""{"reason":"timeout","escalatedTo":{{System.Text.Json.JsonSerializer.Serialize(gate.EscalateToEmail)}}}""", ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await NotifyEscalationAsync(gate, ct);
            _logger.LogWarning("GateTimeoutJob escalated gate node={NodeId} instance={InstanceId}", gate.NodeId, gate.InstanceId);
            return;
        }

        var decision = action switch
        {
            GateTimeoutActions.AutoApprove => GateDecisionStatus.Approved,
            GateTimeoutActions.AutoReject => GateDecisionStatus.Rejected,
            _ => GateDecisionStatus.Escalated,
        };
        gate.Decision = decision;
        gate.DecidedAt = DateTime.UtcNow;
        gate.DecisionNote = action switch
        {
            GateTimeoutActions.AutoApprove => "Auto-approved: no decision before the deadline.",
            GateTimeoutActions.AutoReject => "Auto-rejected: no decision before the deadline.",
            _ => "Timed out with no decision.",
        };
        _gates.Update(gate);
        await AppendDecisionEventAsync(gate, "GateDecided",
            $$"""{"decision":"{{decision}}","reason":"timeout"}""", ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (decision == GateDecisionStatus.Approved)
        {
            _logger.LogInformation("GateTimeoutJob auto-approved gate node={NodeId} instance={InstanceId}", gate.NodeId, gate.InstanceId);
            await _orchestrator.CompleteNodeAsync(gate.InstanceId, gate.NodeId,
                """{"decision":"approved","auto":true}""", "gate-timeout", ct);
            return;
        }

        var reason = decision == GateDecisionStatus.Rejected
            ? "Approval auto-rejected: no decision was made before the deadline."
            : action == GateTimeoutActions.Escalate && gate.EscalationCount > 0
                ? "Approval timed out again after escalation with no decision."
                : "Approval gate timed out with no decision and no escalation target. Set 'On timeout' on the approval step to escalate or auto-decide.";
        _logger.LogWarning("GateTimeoutJob failing gate node={NodeId} instance={InstanceId} action={Action}", gate.NodeId, gate.InstanceId, action);
        await _orchestrator.FailNodeAsync(gate.InstanceId, gate.NodeId, reason, "gate-timeout", ct, retryable: false);
    }

    private async Task AppendDecisionEventAsync(GateDecision gate, string type, string payload, CancellationToken ct)
    {
        var seq = await _events.GetNextSequenceNumberAsync(gate.InstanceId, ct);
        await _events.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = gate.WorkspaceId,
            InstanceId = gate.InstanceId,
            SequenceNumber = seq,
            EventType = type,
            NodeId = gate.NodeId,
            NodeType = nameof(NodeType.HumanGate),
            Payload = payload,
            OccurredAt = DateTime.UtcNow,
        }, ct);
    }

    private async Task NotifyEscalationAsync(GateDecision gate, CancellationToken ct)
    {
        if (_email is null || string.IsNullOrWhiteSpace(gate.EscalateToEmail)) return;
        try
        {
            var platformUrl = _config?["Platform:BaseUrl"] ?? _config?["PLATFORM_URL"] ?? "https://app.flowamaz.com";
            var link = $"{platformUrl.TrimEnd('/')}/gates";
            var html = $"<p>An approval was escalated to you because the original approver did not respond in time.</p>" +
                       $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Review pending approvals</a> " +
                       $"(decide before {gate.ExpiresAt:yyyy-MM-dd HH:mm} UTC).</p>";
            await _email.SendAsync(gate.EscalateToEmail, "Approval escalated to you", html,
                $"An approval was escalated to you. Review it at {link} before {gate.ExpiresAt:yyyy-MM-dd HH:mm} UTC.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The gate is still reassigned and visible in the portal queue; email is best-effort.
            _logger.LogWarning(ex, "GateTimeoutJob escalation email failed gate={GateId}", gate.Id);
        }
    }
}
