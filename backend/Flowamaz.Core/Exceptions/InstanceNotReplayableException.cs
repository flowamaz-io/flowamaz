namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when replaying an instance that has not yet reached a terminal state
/// (Completed/Failed/Cancelled). Maps to 409 Conflict. (prompt 07-04)
/// </summary>
public sealed class InstanceNotReplayableException : AppException
{
    public InstanceNotReplayableException(Guid instanceId, string status)
        : base("INSTANCE_NOT_REPLAYABLE",
            $"Instance '{instanceId}' is still {status} and cannot be replayed. " +
            "Replay is only available once an instance has finished (Completed, Failed, or Cancelled). " +
            "Wait for it to finish or cancel it first, then replay.",
            httpStatusCode: 409)
    { }
}
