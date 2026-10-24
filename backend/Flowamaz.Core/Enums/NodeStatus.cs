namespace Flowamaz.Core.Enums;

/// <summary>Status of a single node within an instance. Stored as a string column.</summary>
public enum NodeStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Compensating,

    /// <summary>The run was cancelled while this node was pending/running (e.g. waiting at a gate).</summary>
    Cancelled,
}
