namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a config value violates a platform invariant — most commonly when a chosen
/// model lacks a capability required by the function (F3 needs vision, F7 needs ≥100k ctx).
/// Surfaces at config-save time (FUNCTIONAL.md §5.4), never at call time.
/// </summary>
public sealed class ConfigViolationException : AppException
{
    public ConfigViolationException(string message)
        : base("CONFIG_VIOLATION", message, httpStatusCode: 422) { }

    public ConfigViolationException(string errorCode, string message)
        : base(errorCode, message, httpStatusCode: 422) { }
}
