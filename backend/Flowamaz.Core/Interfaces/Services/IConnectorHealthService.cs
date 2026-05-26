namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Status = "healthy" | "expired" | "rate_limited" | "error".</summary>
public record ConnectorHealthStatus(
    string ConnectorId,
    string CredentialName,
    string Status,
    DateTime? LastUsedAt,
    string? ErrorMessage,
    DateTime? ExpiresAt,
    int CallsLastHour);

/// <summary>
/// Checks health for all installed connectors in a workspace.
/// </summary>
public interface IConnectorHealthService
{
    Task<IReadOnlyList<ConnectorHealthStatus>> GetHealthAsync(Guid workspaceId, CancellationToken ct = default);
}
