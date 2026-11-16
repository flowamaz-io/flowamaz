namespace Flowamaz.Core.Enums;

/// <summary>
/// Runtime status of a workflow instance. Stored as a string column so the queue's
/// SKIP LOCKED claim query can filter on the readable literal 'Pending'.
/// </summary>
public enum InstanceStatus
{
    Pending,
    Running,
    Waiting,
    Completed,
    Failed,
    Cancelled,
    Compensating,

    /// <summary>
    /// Paused at a Development breakpoint before a node executed (prompt 07-04 / 08-01). Reachable
    /// only in the Development environment; resume transitions back to Running.
    /// </summary>
    BreakpointHit,
}
