namespace Flowamaz.Core.Interfaces.Runtime;

// Plug-in points between the workflow runtime and the features a host adds around it
// (docs/ENGINE-RUNTIME-SPLIT.md §2). The runtime depends only on these; each has a default that
// changes nothing, and the Flowamaz product registers its own implementations (edition limits,
// archived workspaces, audit log, notifications, debugger) in Application/Workflow/Hosting.

/// <summary>Decides whether a new run may start. Throw an actionable exception to refuse it.</summary>
public interface IRunAdmission
{
    Task EnsureCanStartAsync(Guid workspaceId, bool isTest, CancellationToken ct);
}

/// <summary>Receives run lifecycle events (audit, notifications, metrics). Must not throw for business reasons.</summary>
public interface IRunEventSink
{
    Task RunStartedAsync(RunStartedEvent e, CancellationToken ct);
    Task RunCompletedAsync(RunCompletedEvent e, CancellationToken ct);
    Task RunCancelledAsync(RunCancelledEvent e, CancellationToken ct);
    Task GateDecidedAsync(GateDecidedEvent e, CancellationToken ct);

    /// <summary>The run ended Rejected (an approver's "no", B10). Defaults to nothing so existing sinks keep working.</summary>
    Task RunRejectedAsync(RunRejectedEvent e, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Lets a host pause a run before a node executes (for example, a debugger breakpoint).</summary>
public interface IStepInterceptor
{
    bool ShouldPause(Guid workspaceId, Guid workflowId, Guid instanceId, string nodeId);
}

/// <param name="TriggeredByUserId">The signed-in user who started it; null for webhook, API and scheduled triggers.</param>
public sealed record RunStartedEvent(
    Guid WorkspaceId, Guid InstanceId, Guid WorkflowId, string WorkflowName,
    Guid? TriggeredByUserId, string TriggerType, bool IsTest);

/// <param name="CreatedBy">The user who started the run, if any.</param>
/// <param name="WorkflowName">The workflow's name, for messages; null if unknown.</param>
public sealed record RunCompletedEvent(Guid WorkspaceId, Guid InstanceId, Guid? CreatedBy, string? WorkflowName = null);

/// <param name="Reason">The reason the run records, e.g. "Gate rejected: budget too high".</param>
public sealed record RunRejectedEvent(
    Guid WorkspaceId, Guid InstanceId, Guid WorkflowId, string? WorkflowName, Guid? CreatedBy, string NodeId, string Reason, bool IsTest);

/// <param name="ActorUserId">Who cancelled it; null for a public-API call (the host attributes it to the API key).</param>
/// <param name="CancelledByLabel">A name the calling application gave, when there is no user.</param>
public sealed record RunCancelledEvent(
    Guid WorkspaceId, Guid InstanceId, Guid WorkflowId, string? WorkflowName,
    Guid? ActorUserId, string? CancelledByLabel, int ClosedNodes, bool IsTest);

/// <param name="DecidedByUserId">The user who decided; null when decided through the API.</param>
/// <param name="DecidedByLabel">The decider's name given by the calling application, when there is no user.</param>
/// <param name="WorkflowName">The workflow's name, for audit labels and messages; null if unknown.</param>
public sealed record GateDecidedEvent(
    Guid WorkspaceId, Guid GateId, Guid InstanceId, string NodeId, bool Approved,
    Guid? DecidedByUserId, string? DecidedByLabel, string? WorkflowName = null);

/// <summary>Default admission: every run may start.</summary>
public sealed class AllowAllRunAdmission : IRunAdmission
{
    public static readonly AllowAllRunAdmission Instance = new();
    public Task EnsureCanStartAsync(Guid workspaceId, bool isTest, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Default sink: ignores events.</summary>
public sealed class NullRunEventSink : IRunEventSink
{
    public static readonly NullRunEventSink Instance = new();
    public Task RunStartedAsync(RunStartedEvent e, CancellationToken ct) => Task.CompletedTask;
    public Task RunCompletedAsync(RunCompletedEvent e, CancellationToken ct) => Task.CompletedTask;
    public Task RunCancelledAsync(RunCancelledEvent e, CancellationToken ct) => Task.CompletedTask;
    public Task GateDecidedAsync(GateDecidedEvent e, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Default interceptor: never pauses.</summary>
public sealed class NoPauseStepInterceptor : IStepInterceptor
{
    public static readonly NoPauseStepInterceptor Instance = new();
    public bool ShouldPause(Guid workspaceId, Guid workflowId, Guid instanceId, string nodeId) => false;
}
