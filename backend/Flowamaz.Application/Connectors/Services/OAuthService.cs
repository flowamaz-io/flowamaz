using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// OAuth 2.0 authorization-code flow implementation.
/// Initiate: builds authorization URL, stores CSRF state in Redis (10-min TTL).
/// Callback: validates state, exchanges code for real access token, stores token via vault.
/// </summary>
public sealed class OAuthService : IOAuthService
{
    public const string HttpClientName = "oauth-exchange";

    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly IConnectionMultiplexer _redis;
    private readonly ICredentialVaultService _vault;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthService> _logger;

    public OAuthService(
        IConnectionMultiplexer redis,
        ICredentialVaultService vault,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OAuthService> logger)
    {
        _redis = redis;
        _vault = vault;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OAuthInitiateResult> InitiateAsync(Guid workspaceId, string connectorId, CancellationToken ct = default)
    {
        _logger.LogInformation("[OAuthService] InitiateAsync entry connector={ConnectorId}", connectorId);

        var platformUrl = _configuration["Platform:BaseUrl"] ?? "https://flowamaz.io";
        var state = Guid.NewGuid().ToString("N");

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

        var accessToken = await ExchangeCodeAsync(stateData.ConnectorId, code, ct);

        var credentialId = await _vault.StoreAsync(
            stateData.WorkspaceId,
            stateData.ConnectorId,
            $"{stateData.ConnectorId}-oauth-{DateTime.UtcNow:yyyyMMdd}",
            "OAuth2AuthCode",
            accessToken,
            ct);

        _logger.LogInformation("[OAuthService] HandleCallbackAsync exit credential={CredentialId}", credentialId);
        return credentialId;
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private async Task<string> ExchangeCodeAsync(string connectorId, string code, CancellationToken ct)
    {
        var platformUrl = _configuration["Platform:BaseUrl"] ?? "https://flowamaz.io";
        var redirectUri = $"{platformUrl}/api/v1/oauth/callback";
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        return connectorId switch
        {
            "slack" => await ExchangeSlackAsync(httpClient, code, redirectUri, ct),
            "github" => await ExchangeGitHubAsync(httpClient, code, ct),
            "microsoft-teams" => await ExchangeMicrosoftAsync(httpClient, code, redirectUri, ct),
            _ => throw new NotSupportedException(
                $"OAuth token exchange is not supported for connector '{connectorId}'. " +
                "Supported connectors: slack, github, microsoft-teams.")
        };
    }

    private async Task<string> ExchangeSlackAsync(
        HttpClient httpClient, string code, string redirectUri, CancellationToken ct)
    {
        var clientId = _configuration["SLACK_CLIENT_ID"]
            ?? _configuration["Connectors:Slack:ClientId"]
            ?? throw new InvalidOperationException(
                "Slack OAuth is not configured. Set SLACK_CLIENT_ID and SLACK_CLIENT_SECRET.");
        var clientSecret = _configuration["SLACK_CLIENT_SECRET"]
            ?? throw new InvalidOperationException(
                "Slack OAuth client secret is not configured. Set SLACK_CLIENT_SECRET.");

        var response = await httpClient.PostAsync(
            "https://slack.com/api/oauth.v2.access",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri
            }), ct);

        var slackTokens = await response.Content.ReadFromJsonAsync<SlackOAuthResponse>(ct)
            ?? throw new OAuthException("Slack token exchange returned an empty response.");

        if (!slackTokens.Ok)
            throw new OAuthException(
                $"Slack token exchange failed: {slackTokens.Error}. Check your Slack app configuration.");

        return slackTokens.AccessToken
            ?? throw new OAuthException("Slack token exchange succeeded but returned no access_token.");
    }

    private async Task<string> ExchangeGitHubAsync(
        HttpClient httpClient, string code, CancellationToken ct)
    {
        var clientId = _configuration["GITHUB_CLIENT_ID"]
            ?? _configuration["Connectors:GitHub:ClientId"]
            ?? throw new InvalidOperationException(
                "GitHub OAuth is not configured. Set GITHUB_CLIENT_ID and GITHUB_CLIENT_SECRET.");
        var clientSecret = _configuration["GITHUB_CLIENT_SECRET"]
            ?? throw new InvalidOperationException(
                "GitHub OAuth client secret is not configured. Set GITHUB_CLIENT_SECRET.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        request.Headers.Add("Accept", "application/json");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        var response = await httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        // Accept: application/json header makes GitHub return JSON.
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("error", out var errorProp))
            throw new OAuthException(
                $"GitHub token exchange failed: {errorProp.GetString()}. Check your GitHub OAuth app configuration.");

        var accessToken = doc.RootElement.TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;

        return accessToken
            ?? throw new OAuthException("GitHub token exchange succeeded but returned no access_token.");
    }

    private async Task<string> ExchangeMicrosoftAsync(
        HttpClient httpClient, string code, string redirectUri, CancellationToken ct)
    {
        var clientId = _configuration["MICROSOFT_CLIENT_ID"]
            ?? _configuration["Connectors:MicrosoftTeams:ClientId"]
            ?? throw new InvalidOperationException(
                "Microsoft OAuth is not configured. Set MICROSOFT_CLIENT_ID and MICROSOFT_CLIENT_SECRET.");
        var clientSecret = _configuration["MICROSOFT_CLIENT_SECRET"]
            ?? throw new InvalidOperationException(
                "Microsoft OAuth client secret is not configured. Set MICROSOFT_CLIENT_SECRET.");
        var tenantId = _configuration["MICROSOFT_TENANT_ID"] ?? "common";

        var response = await httpClient.PostAsync(
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["scope"] = "https://graph.microsoft.com/.default offline_access"
            }), ct);

        var msTokens = await response.Content.ReadFromJsonAsync<MicrosoftOAuthResponse>(ct)
            ?? throw new OAuthException("Microsoft token exchange returned an empty response.");

        if (string.IsNullOrEmpty(msTokens.AccessToken))
            throw new OAuthException(
                "Microsoft token exchange succeeded but returned no access_token. Check the requested scopes.");

        return msTokens.AccessToken;
    }

    private string BuildAuthorizationUrl(string connectorId, string state, string platformUrl)
    {
        var redirectUri = $"{platformUrl}/api/v1/oauth/callback";

        return connectorId switch
        {
            "slack" => BuildSlackUrl(state, redirectUri),
            "github" => BuildGitHubUrl(state, redirectUri),
            "microsoft-teams" => BuildTeamsUrl(state),
            _ => throw new NotSupportedException(
                $"Connector '{connectorId}' does not support OAuth. " +
                "Supported connectors: slack, github, microsoft-teams.")
        };
    }

    private string BuildSlackUrl(string state, string redirectUri)
    {
        var clientId = _configuration["SLACK_CLIENT_ID"]
            ?? _configuration["Connectors:Slack:ClientId"]
            ?? throw new InvalidOperationException("Slack:ClientId is not configured. Set SLACK_CLIENT_ID.");
        return $"https://slack.com/oauth/v2/authorize?client_id={clientId}&scope=channels:read,chat:write&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    }

    private string BuildGitHubUrl(string state, string redirectUri)
    {
        var clientId = _configuration["GITHUB_CLIENT_ID"]
            ?? _configuration["Connectors:GitHub:ClientId"]
            ?? throw new InvalidOperationException("GitHub:ClientId is not configured. Set GITHUB_CLIENT_ID.");
        return $"https://github.com/login/oauth/authorize?client_id={clientId}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    }

    private string BuildTeamsUrl(string state)
    {
        var clientId = _configuration["MICROSOFT_CLIENT_ID"]
            ?? _configuration["Connectors:MicrosoftTeams:ClientId"]
            ?? throw new InvalidOperationException("Microsoft:ClientId is not configured. Set MICROSOFT_CLIENT_ID.");
        return $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&state={state}";
    }

    private sealed record OAuthStateData(Guid WorkspaceId, string ConnectorId);
}

// ─── OAuth response DTOs (internal to the exchange flow) ─────────────────────

internal sealed class SlackOAuthResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("bot_user_id")]
    public string? BotUserId { get; init; }

    [JsonPropertyName("team")]
    public SlackTeam? Team { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class SlackTeam
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class MicrosoftOAuthResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }
}
