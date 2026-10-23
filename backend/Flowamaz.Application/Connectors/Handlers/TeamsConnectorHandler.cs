using System.Text.Json;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Teams channel message through Microsoft Graph, using the workspace's Microsoft OAuth credential
/// (delegated ChannelMessage.Send). Input: { team_id, channel_id, text }.
/// </summary>
public sealed class TeamsSendMessageHandler : IConnectorOperationHandler
{
    public const string HttpClientName = "teams-connector";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TeamsSendMessageHandler> _logger;

    public TeamsSendMessageHandler(IHttpClientFactory httpClientFactory, ILogger<TeamsSendMessageHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "microsoft-teams";
    public string OperationId => "send-message";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Teams:send-message] ExecuteAsync entry");
        const string op = "microsoft-teams/send-message";
        var root = input.RootElement;
        var cred = ConnectorInput.RequireCredential(credential, "Microsoft Teams", op);
        var teamId = ConnectorInput.Segment(ConnectorInput.Required(root, "team_id", op));
        var channelId = ConnectorInput.Segment(ConnectorInput.Required(root, "channel_id", op));
        var text = ConnectorInput.Required(root, "text", op);

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = ConnectorHttp.Json(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/teams/{teamId}/channels/{channelId}/messages",
            new { body = new { contentType = "html", content = text } }, cred.AccessToken);
        var result = await ConnectorHttp.SendAsync(client, request, op, ct);

        _logger.LogInformation("[Teams:send-message] ExecuteAsync exit");
        return result;
    }
}

/// <summary>Teams message through an incoming-webhook (Workflows) URL. Input: { webhook_url, text }.</summary>
public sealed class TeamsSendWebhookMessageHandler : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TeamsSendWebhookMessageHandler> _logger;

    public TeamsSendWebhookMessageHandler(IHttpClientFactory httpClientFactory, ILogger<TeamsSendWebhookMessageHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "microsoft-teams";
    public string OperationId => "send-webhook-message";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Teams:send-webhook-message] ExecuteAsync entry");
        const string op = "microsoft-teams/send-webhook-message";
        var root = input.RootElement;
        var url = EgressGuard.RequireHttpUrl(ConnectorInput.Optional(root, "webhook_url"), "webhook_url");
        var text = ConnectorInput.Required(root, "text", op);

        using var client = _httpClientFactory.CreateClient(TeamsSendMessageHandler.HttpClientName);
        using var request = ConnectorHttp.Json(HttpMethod.Post, url.ToString(), new { text });
        var result = await ConnectorHttp.SendAsync(client, request, op, ct);

        _logger.LogInformation("[Teams:send-webhook-message] ExecuteAsync exit");
        return result;
    }
}

/// <summary>
/// Posts an Adaptive Card with Approve/Reject actions for a human-gate decision via Teams webhook.
/// Input: { webhook_url, gate_id, gate_summary, platform_url }
/// </summary>
public sealed class TeamsPostAdaptiveCardHandler : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TeamsPostAdaptiveCardHandler> _logger;

    public TeamsPostAdaptiveCardHandler(IHttpClientFactory httpClientFactory, ILogger<TeamsPostAdaptiveCardHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "microsoft-teams";
    public string OperationId => "post-adaptive-card";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Teams:post-adaptive-card] ExecuteAsync entry");
        const string op = "microsoft-teams/post-adaptive-card";
        var root = input.RootElement;
        var url = EgressGuard.RequireHttpUrl(ConnectorInput.Optional(root, "webhook_url"), "webhook_url");
        var gateId = ConnectorInput.Required(root, "gate_id", op);
        var gateSummary = ConnectorInput.Optional(root, "gate_summary") ?? string.Empty;
        var platformUrl = ConnectorInput.Optional(root, "platform_url") ?? string.Empty;

        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new { contentType = "application/vnd.microsoft.card.adaptive", content = BuildAdaptiveCard(gateId, gateSummary, platformUrl) }
            }
        };

        using var client = _httpClientFactory.CreateClient(TeamsSendMessageHandler.HttpClientName);
        using var request = ConnectorHttp.Json(HttpMethod.Post, url.ToString(), payload);
        var result = await ConnectorHttp.SendAsync(client, request, op, ct);

        _logger.LogInformation("[Teams:post-adaptive-card] ExecuteAsync exit");
        return result;
    }

    private static object BuildAdaptiveCard(string gateId, string gateSummary, string platformUrl) => new
    {
        type = "AdaptiveCard",
        version = "1.4",
        body = new object[]
        {
            new { type = "TextBlock", text = "Workflow Approval Required", weight = "Bolder", size = "Medium" },
            new { type = "TextBlock", text = gateSummary, wrap = true }
        },
        actions = new object[]
        {
            new { type = "Action.OpenUrl", title = "Approve", url = $"{platformUrl}/gates/{Uri.EscapeDataString(gateId)}/approve" },
            new { type = "Action.OpenUrl", title = "Reject",  url = $"{platformUrl}/gates/{Uri.EscapeDataString(gateId)}/reject" }
        }
    };
}
