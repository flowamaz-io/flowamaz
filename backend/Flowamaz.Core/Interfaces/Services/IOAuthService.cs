namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// OAuth 2.0 authorization-code flow: initiate (redirect) and callback (exchange + store token).
/// </summary>
public interface IOAuthService
{
    /// <summary>
    /// Builds the authorization URL for the given connector and stores the CSRF state in Redis.
    /// </summary>
    Task<OAuthInitiateResult> InitiateAsync(Guid workspaceId, string connectorId, CancellationToken ct = default);

    /// <summary>
    /// Validates state, exchanges code for tokens, persists via vault.
    /// Returns the newly-created credential ID.
    /// </summary>
    Task<Guid> HandleCallbackAsync(string code, string state, CancellationToken ct = default);
}

public record OAuthInitiateResult(string AuthorizationUrl, string State);
