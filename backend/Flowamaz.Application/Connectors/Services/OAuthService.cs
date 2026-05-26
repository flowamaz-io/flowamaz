using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// OAuth 2.0 authorization-code flow implementation.
/// Initiate: builds authorization URL, stores CSRF state in Redis (10-min TTL).
/// Callback: validates state, exchanges code (mock for Phase 4), stores token via vault.
/// </summary>
public sealed class OAuthService : IOAuthService
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly IConnectionMultiplexer _redis;
    private readonly ICredentialVaultService _vault;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthService> _logger;

    public OAuthService(
        IConnectionMultiplexer redis,
        ICredentialVaultService vault,
        IConfiguration configuration,
        ILogger<OAuthService> logger)
    {
        _redis = redis;
        _vault = vault;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OAuthInitiateResult> InitiateAsync(Guid workspaceId, string connectorId, CancellationToken ct = default)
    {
        _logger.LogInformation("[OAuthService] InitiateAsync entry connector={ConnectorId}", connectorId);

        var platformUrl = _configuration["Platform:BaseUrl"] ?? "https://flowamaz.io";
        var state = Guid.NewGuid().ToString("N");

        // Store state → workspaceId + connectorId in Redis
        var db = _redis.GetDatabase();
        var statePayload = JsonSerializer.Serialize(new OAuthStateData(workspaceId, connectorId));
        await db.StringSetAsync($"oauth:state:{state}", statePayload, StateTtl);

        var authUrl = BuildAuthorizationUrl(connectorId, state, platformUrl);

        _logger.LogInformation("[OAuthService] InitiateAsync exit connector={ConnectorId}", connectorId);
        return new OAuthInitiateResult(authUrl, state);
    }

    public async Task<Guid> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
    {
        _logger.LogInformation("[OAuthService] HandleCallbackAsync entry");

        var db = _redis.GetDatabase();
        var stateKey = $"oauth:state:{state}";
        var statePayload = await db.StringGetDeleteAsync(stateKey);

        if (!statePayload.HasValue)
            throw new InvalidOperationException("OAuth state not found or expired. Restart the OAuth flow.");

        var stateData = JsonSerializer.Deserialize<OAuthStateData>(statePayload.ToString())
            ?? throw new InvalidOperationException("OAuth state payload is corrupt.");

        // Phase 4 mock: exchange code → dummy token. Real implementation calls token endpoint.
        var mockToken = $"mock_token_{code}_{DateTime.UtcNow.Ticks}";

        var credentialId = await _vault.StoreAsync(
            stateData.WorkspaceId,
            stateData.ConnectorId,
            $"{stateData.ConnectorId}-oauth-{DateTime.UtcNow:yyyyMMdd}",
            "OAuth2AuthCode",
            mockToken,
            ct);

        _logger.LogInformation("[OAuthService] HandleCallbackAsync exit credential={CredentialId}", credentialId);
        return credentialId;
    }

    private string BuildAuthorizationUrl(string connectorId, string state, string platformUrl)
    {
        var redirectUri = $"{platformUrl}/api/v1/oauth/callback";

        return connectorId switch
        {
            "slack" => BuildSlackUrl(state, redirectUri),
            "github" => BuildGitHubUrl(state, redirectUri),
            "microsoft-teams" => BuildTeamsUrl(state),
            _ => throw new InvalidOperationException($"Connector '{connectorId}' does not support OAuth.")
        };
    }

    private string BuildSlackUrl(string state, string redirectUri)
    {
        var clientId = _configuration["Connectors:Slack:ClientId"]
            ?? throw new InvalidOperationException("Connectors:Slack:ClientId is not configured.");
        return $"https://slack.com/oauth/v2/authorize?client_id={clientId}&scope=channels:read,chat:write&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    }

    private string BuildGitHubUrl(string state, string redirectUri)
    {
        var clientId = _configuration["Connectors:GitHub:ClientId"]
            ?? throw new InvalidOperationException("Connectors:GitHub:ClientId is not configured.");
        return $"https://github.com/login/oauth/authorize?client_id={clientId}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    }

    private string BuildTeamsUrl(string state)
    {
        var clientId = _configuration["Connectors:MicrosoftTeams:ClientId"]
            ?? throw new InvalidOperationException("Connectors:MicrosoftTeams:ClientId is not configured.");
        return $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&state={state}";
    }

    private sealed record OAuthStateData(Guid WorkspaceId, string ConnectorId);
}
