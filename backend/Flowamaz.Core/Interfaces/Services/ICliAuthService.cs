using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// OAuth-style device flow for the `fmz` CLI (FUNCTIONAL.md §15). The CLI starts a flow, the person approves it in the
/// browser where they are already signed in, and the CLI polls until approved. No token passes through the flow: the
/// approval records who approved, and the API then creates a CLI session of its own for that person.
/// </summary>
public interface ICliAuthService
{
    /// <summary>
    /// Starts a device flow: generates device + user codes and stores them pending. <paramref name="clientName"/> is
    /// what the CLI calls itself (usually the host name), shown on the approval page and in Settings → CLI sessions.
    /// </summary>
    Task<CliDeviceAuthorization> InitiateDeviceFlowAsync(string? clientName = null, CancellationToken ct = default);

    /// <summary>The name the CLI gave for a pending flow, for the approval page. Null if the code is unknown or expired.</summary>
    Task<string?> GetPendingClientNameAsync(string userCode, CancellationToken ct = default);

    /// <summary>
    /// Approves a pending flow identified by <paramref name="userCode"/> on behalf of <paramref name="orgUserId"/>.
    /// Returns false if the user code is unknown or expired. A code can be approved once.
    /// </summary>
    Task<bool> ApproveAsync(string userCode, Guid orgUserId, CancellationToken ct = default);

    /// <summary>Polls a device code. Pending until approved; then returns the approval once.</summary>
    Task<CliTokenResult> PollTokenAsync(string deviceCode, CancellationToken ct = default);
}
