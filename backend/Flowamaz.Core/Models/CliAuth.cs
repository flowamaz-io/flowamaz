namespace Flowamaz.Core.Models;

/// <summary>Device-flow authorization handed to the CLI on `fmz login` (FUNCTIONAL.md §14).</summary>
public sealed record CliDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int IntervalSeconds,
    int ExpiresInSeconds);

/// <summary>Result of polling the CLI token endpoint. Pending until the user approves in the browser.</summary>
public sealed record CliTokenResult(bool Pending, string? AccessToken, DateTime? ExpiresAt)
{
    public static CliTokenResult Waiting() => new(true, null, null);
    public static CliTokenResult Ready(string accessToken, DateTime expiresAt) => new(false, accessToken, expiresAt);
}
