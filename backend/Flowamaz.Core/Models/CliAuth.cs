namespace Flowamaz.Core.Models;

/// <summary>Device-flow authorization handed to the CLI on `fmz login` (FUNCTIONAL.md §14).</summary>
public sealed record CliDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int IntervalSeconds,
    int ExpiresInSeconds);

/// <summary>
/// Result of polling a device code. Pending until the person approves in the browser; then it carries who approved
/// (and the name the CLI gave itself) exactly once, and the API creates a CLI session for that person.
/// </summary>
public sealed record CliTokenResult(bool Pending, Guid? ApprovedByUserId, string? ClientName)
{
    public static CliTokenResult Waiting() => new(true, null, null);
    public static CliTokenResult Approved(Guid orgUserId, string? clientName) => new(false, orgUserId, clientName);
}
