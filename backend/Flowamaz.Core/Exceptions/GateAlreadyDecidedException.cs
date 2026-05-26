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
