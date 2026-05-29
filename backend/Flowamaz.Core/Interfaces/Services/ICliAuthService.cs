using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// OAuth-style device flow for the `fmz` CLI (FUNCTIONAL.md §14). The CLI starts a flow, the user
/// approves it in the browser (already authenticated in the SPA), and the CLI polls for the token.
/// </summary>
public interface ICliAuthService
{
    /// <summary>Starts a device flow: generates device + user codes and stores them pending.</summary>
    Task<CliDeviceAuthorization> InitiateDeviceFlowAsync(CancellationToken ct = default);

    /// <summary>
    /// Approves a pending flow identified by <paramref name="userCode"/>, binding the supplied
    /// access token (the approving user's) so the CLI can collect it. Returns false if the user
    /// code is unknown or expired.
    /// </summary>
    Task<bool> ApproveAsync(string userCode, string accessToken, DateTime expiresAt, CancellationToken ct = default);

    /// <summary>Polls a device code. Pending until approved; then returns the token once.</summary>
    Task<CliTokenResult> PollTokenAsync(string deviceCode, CancellationToken ct = default);
}
