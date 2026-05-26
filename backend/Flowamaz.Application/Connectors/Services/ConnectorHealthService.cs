using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// Reports health of installed connectors for a workspace.
/// Status: "healthy" | "expired" | "rate_limited" | "error"
/// </summary>
public sealed class ConnectorHealthService : IConnectorHealthService
{
    private readonly IConnectorCatalogueService _catalogue;
    private readonly ICredentialVaultService _vault;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ConnectorHealthService> _logger;

    public ConnectorHealthService(
        IConnectorCatalogueService catalogue,
        ICredentialVaultService vault,
        IConnectionMultiplexer redis,
        ILogger<ConnectorHealthService> logger)
    {
        _catalogue = catalogue;
        _vault = vault;
        _redis = redis;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConnectorHealthStatus>> GetHealthAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("[ConnectorHealthService] GetHealthAsync entry workspace={WorkspaceId}", workspaceId);

        var installed = await _catalogue.GetInstalledAsync(workspaceId, ct);
        var credentials = await _vault.ListAsync(workspaceId, ct);
        var credentialMap = credentials.ToDictionary(c => c.Id);

        var db = _redis.GetDatabase();
        var now = DateTime.UtcNow;
        var currentHour = now.ToString("yyyyMMddHH");

        var results = new List<ConnectorHealthStatus>(installed.Count);

        foreach (var wc in installed)
        {
            var connectorId = wc.ConnectorDefinition?.ConnectorId ?? wc.ConnectorDefinitionId.ToString();

            if (wc.CredentialId is null)
            {
                results.Add(new ConnectorHealthStatus(
                    connectorId, "no-credential", "healthy", null, null, null, 0));
                continue;
            }

            credentialMap.TryGetValue(wc.CredentialId.Value, out var cred);

            if (cred is null)
            {
                results.Add(new ConnectorHealthStatus(
                    connectorId, "unknown", "error", null, "Credential not found.", null, 0));
                continue;
            }

            // Rate-limit counter
            var rateLimitKey = $"connector:calls:{workspaceId}:{wc.CredentialId.Value}:{currentHour}";
            var callsRaw = await db.StringGetAsync(rateLimitKey);
            var callsLastHour = callsRaw.HasValue && int.TryParse(callsRaw.ToString(), out var c) ? c : 0;

            // Determine status
            string status;
            string? errorMessage = null;

            if (cred.ExpiresAt.HasValue && cred.ExpiresAt.Value < now)
            {
                status = "expired";
                errorMessage = $"Credential expired at {cred.ExpiresAt.Value:u}.";
            }
            else if (!cred.IsActive)
            {
                status = "error";
                errorMessage = "Credential is revoked.";
            }
            else
            {
                status = "healthy";
            }

            results.Add(new ConnectorHealthStatus(
                connectorId,
                cred.Name,
                status,
                LastUsedAt: null, // LastUsedAt not tracked on CredentialAlias — can be added when needed
                errorMessage,
                cred.ExpiresAt,
                callsLastHour));
        }

        _logger.LogInformation("[ConnectorHealthService] GetHealthAsync exit count={Count}", results.Count);
        return results;
    }
}
