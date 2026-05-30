namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Opens a pull request against the flowamaz-io/connectors repository for a community connector
/// submission. Uses the platform GitHub App credentials.
/// </summary>
public interface IConnectorSubmissionPrService
{
    /// <summary>Creates the PR and returns its HTML URL.</summary>
    Task<string> CreatePullRequestAsync(string connectorName, string manifestYaml, string submitterOrgId, CancellationToken cancellationToken = default);
}
