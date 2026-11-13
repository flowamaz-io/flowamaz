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

/// <summary>An approval whose form answers don't pass the gate's form. Maps to 422; nothing was recorded.</summary>
public sealed class GateFormException : AppException
{
    public GateFormException(IReadOnlyList<string> problems)
        : base("GATE_FORM_INVALID",
            $"This approval needs a few details before it can go through: {string.Join(" ", problems)} Fill them in and approve again.",
            httpStatusCode: 422)
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}
