using System.Text;
using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>Sends a plain text message via Microsoft Teams incoming webhook.</summary>
public sealed class TeamsSendMessageHandler : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TeamsSendMessageHandler> _logger;

    public TeamsSendMessageHandler(IHttpClientFactory httpClientFactory, ILogger<TeamsSendMessageHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "microsoft-teams";
    public string OperationId => "send-message";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Teams:send-message] ExecuteAsync entry");

        var root = input.RootElement;
        var webhookUrl = root.GetProperty("webhook_url").GetString() ?? throw new InvalidOperationException("'webhook_url' is required.");
        var text = root.GetProperty("text").GetString() ?? throw new InvalidOperationException("'text' is required.");

        var body = JsonSerializer.Serialize(new { text });
        using var client = _httpClientFactory.CreateClient("teams-connector");
        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);
        _logger.LogInformation("[Teams:send-message] ExecuteAsync exit status={Status}", (int)response.StatusCode);

        return JsonDocument.Parse($"{{\"success\":{response.IsSuccessStatusCode.ToString().ToLowerInvariant()},\"status_code\":{(int)response.StatusCode}}}");
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

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Teams:post-adaptive-card] ExecuteAsync entry");

        var root = input.RootElement;
        var webhookUrl = root.GetProperty("webhook_url").GetString() ?? throw new InvalidOperationException("'webhook_url' is required.");
        var gateId = root.GetProperty("gate_id").GetString() ?? throw new InvalidOperationException("'gate_id' is required.");
        var gateSummary = root.GetProperty("gate_summary").GetString() ?? "";
        var platformUrl = root.GetProperty("platform_url").GetString() ?? "";

        var card = BuildAdaptiveCard(gateId, gateSummary, platformUrl);
        var payload = JsonSerializer.Serialize(new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = card
                }
            }
        });

        using var client = _httpClientFactory.CreateClient("teams-connector");
        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);
        _logger.LogInformation("[Teams:post-adaptive-card] ExecuteAsync exit status={Status}", (int)response.StatusCode);

        return JsonDocument.Parse($"{{\"success\":{response.IsSuccessStatusCode.ToString().ToLowerInvariant()},\"status_code\":{(int)response.StatusCode}}}");
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
            new { type = "Action.OpenUrl", title = "Approve", url = $"{platformUrl}/gates/{gateId}/approve" },
            new { type = "Action.OpenUrl", title = "Reject",  url = $"{platformUrl}/gates/{gateId}/reject" }
        }
    };
}
