using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Drives saga compensation when a node fails terminally. Sets the instance into Compensating,
/// runs the chosen <see cref="SagaStrategyType"/>, and records CompensationStarted then
/// CompensationCompleted/CompensationFailed events. Compensation failures never throw — they are
/// logged and the engine continues with the remaining steps (FUNCTIONAL.md §2.7).
/// </summary>
public interface ISagaEngine
{
    Task StartAsync(Guid instanceId, string failedNodeId, SagaStrategyType strategy, CancellationToken cancellationToken = default);
}
