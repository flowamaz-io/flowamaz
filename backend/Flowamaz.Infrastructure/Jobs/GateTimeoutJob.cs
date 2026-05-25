using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 60s) that times out pending human-approval gates. An expired gate is marked
/// Escalated and a GateDecided event is appended; if it has an escalation target the gate waits on
/// that target, otherwise the gate node is failed which triggers the instance's saga.
/// </summary>
[DisallowConcurrentExecution]
public sealed class GateTimeoutJob : IJob
{
    private readonly IGateDecisionRepository _gates;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GateTimeoutJob> _logger;

    public GateTimeoutJob(
        IGateDecisionRepository gates,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        IUnitOfWork unitOfWork,
        ILogger<GateTimeoutJob> logger)
    {
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
                gate.Decision = GateDecisionStatus.Escalated;
                gate.DecidedAt = DateTime.UtcNow;
                _gates.Update(gate);

                var seq = await _events.GetNextSequenceNumberAsync(gate.InstanceId, context.CancellationToken);
                await _events.AppendAsync(new WorkflowEvent
                {
                    WorkspaceId = gate.WorkspaceId,
                    InstanceId = gate.InstanceId,
                    SequenceNumber = seq,
                    EventType = "GateDecided",
                    NodeId = gate.NodeId,
                    NodeType = nameof(NodeType.HumanGate),
                    Payload = """{"decision":"Escalated","reason":"timeout"}""",
                    OccurredAt = DateTime.UtcNow,
                }, context.CancellationToken);
                await _unitOfWork.SaveChangesAsync(context.CancellationToken);

                if (gate.EscalatedTo is not null)
                {
                    _logger.LogWarning(
                        "GateTimeoutJob escalated gate node={NodeId} instance={InstanceId} to {EscalatedTo}",
                        gate.NodeId, gate.InstanceId, gate.EscalatedTo);
                }
                else
                {
                    _logger.LogWarning(
                        "GateTimeoutJob no escalation target for gate node={NodeId} instance={InstanceId} — failing the gate",
                        gate.NodeId, gate.InstanceId);
                    await _orchestrator.FailNodeAsync(gate.InstanceId, gate.NodeId, "Approval gate timed out with no escalation target.", "gate-timeout", context.CancellationToken);
                }
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
}
