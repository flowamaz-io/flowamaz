namespace Flowamaz.Core.Enums;

/// <summary>
/// Saga compensation strategies (FUNCTIONAL.md §2.7). Backward undoes completed work in reverse;
/// Forward retries the failed node and continues; Pivot compensates before the failure point then
/// retries forward.
/// </summary>
public enum SagaStrategyType
{
    Backward,
    Forward,
    Pivot,
}
