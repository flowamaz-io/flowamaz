namespace Flowamaz.Core.Exceptions;

/// <summary>Thrown when deciding a gate that is no longer pending. Maps to 409.</summary>
public sealed class GateAlreadyDecidedException : AppException
{
    public GateAlreadyDecidedException(string nodeId)
        : base("GATE_ALREADY_DECIDED",
            $"Gate '{nodeId}' has already been decided. Refresh to see its current decision.",
            httpStatusCode: 409)
    { }
}

/// <summary>The approval belongs to a run that was cancelled or has already finished.</summary>
public sealed class GateRunEndedException : AppException
{
    public GateRunEndedException(string nodeId)
        : base("GATE_RUN_ENDED",
            $"The run waiting on '{nodeId}' was cancelled or has already finished, so this approval no longer applies. Refresh the Gates list.",
            httpStatusCode: 409)
    { }
}
