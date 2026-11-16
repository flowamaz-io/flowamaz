namespace Flowamaz.Core.Enums;

/// <summary>Compensation (saga) state for an instance. Stored as a string column.</summary>
public enum SagaState
{
    None,
    Compensating,
    CompensationFailed,
}
