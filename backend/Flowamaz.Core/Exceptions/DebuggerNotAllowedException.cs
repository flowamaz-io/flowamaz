namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a step-debugger operation targets a Production instance. The debugger is restricted
/// to Dev/Staging (FUNCTIONAL.md §8.10). Maps to 403.
/// </summary>
public sealed class DebuggerNotAllowedException : AppException
{
    public DebuggerNotAllowedException()
        : base("DEBUGGER_PRODUCTION_FORBIDDEN",
            "The step debugger is only available for Dev and Staging instances. Production runs cannot be paused or altered.",
            httpStatusCode: 403)
    { }
}
