namespace Flowamaz.Core.Models;

/// <summary>
/// Outcome of validating an incoming API key — returned only when the key is active and unexpired.
/// Carries just enough to scope the request: the workspace, environment, granted scopes and key id.
/// </summary>
public sealed record WorkspaceApiKeyValidationResult(
    Guid KeyId,
    Guid WorkspaceId,
    Guid EnvironmentId,
    IReadOnlyList<string> Scopes,
    string? Name = null,
    string? KeyPrefix = null)
{
    /// <summary>
    /// Non-secret audit label for actions taken with this key, e.g. <c>API key "Loan app" (fmz_live_finops_…)</c>.
    /// Uses only the display name and the stored display prefix — never the key itself.
    /// </summary>
    public string AuditLabel =>
        $"API key \"{(string.IsNullOrWhiteSpace(Name) ? KeyId.ToString("N")[..8] : Name)}\"" +
        (string.IsNullOrWhiteSpace(KeyPrefix) ? string.Empty : $" ({KeyPrefix}…)");
}
