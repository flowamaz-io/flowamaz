namespace Flowamaz.Core.Exceptions;

/// <summary>An agreed public API limit outside what Flowamaz accepts. Maps to HTTP 400.</summary>
public sealed class ApiLimitOutOfRangeException : AppException
{
    public ApiLimitOutOfRangeException(int max)
        : base(
            "API_LIMIT_OUT_OF_RANGE",
            $"The limit must be between 0 and {max:N0} calls a minute (0 means no per-minute limit). Enter a number in that " +
            "range, or clear it to use the plan's limit.")
    {
    }
}
