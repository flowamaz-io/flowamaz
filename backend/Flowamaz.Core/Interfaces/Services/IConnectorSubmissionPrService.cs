namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Opens a pull request against the flowamaz-io/connectors repository for a community connector
/// submission. Uses the platform GitHub App credentials.
/// </summary>
public interface IConnectorSubmissionPrService
{
    /// <summary>
    /// Creates the PR and returns its HTML URL. The manifest is committed as
    /// <c>connectors/{publisherId}/{connectorId}/connector.yaml</c>, the layout the Library sync reads
    /// (FUNCTIONAL.md §23.3), so the connector goes live once the PR merges.
    /// </summary>
    Task<string> CreatePullRequestAsync(string connectorName, string connectorId, string publisherId, string manifestYaml,
        string submitterOrgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The pull request's state: "open", "merged" or "closed" (closed without merging), or null when GitHub can't be
    /// asked (no token, network, rate limit). Callers keep the last known status on null.
    /// </summary>
    Task<string?> GetPullRequestStateAsync(string pullRequestUrl, CancellationToken cancellationToken = default);
}
