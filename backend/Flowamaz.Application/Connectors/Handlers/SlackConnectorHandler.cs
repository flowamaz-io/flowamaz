using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Shared Slack Web API call. Uses the workspace's Slack OAuth (bot) token from the credential.
/// Slack returns HTTP 200 with <c>"ok": false</c> for most errors, so the body is checked too.
/// </summary>
public abstract class SlackHandlerBase : IConnectorOperationHandler
{
    public const string HttpClientName = "slack-connector";
    private const string PostMessageUrl = "https://slack.com/api/chat.postMessage";

    private readonly IHttpClientFactory _httpClientFactory;
    protected SlackHandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    protected ILogger Logger { get; }
    public string ConnectorId => "slack";
    public abstract string OperationId { get; }
    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct);

    protected async Task<JsonDocument> PostMessageAsync(ConnectorCredential? credential, object body, CancellationToken ct)
    {
        var op = $"slack/{OperationId}";
        var cred = ConnectorInput.RequireCredential(credential, "Slack", op);
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = ConnectorHttp.Json(HttpMethod.Post, PostMessageUrl, body, cred.AccessToken);
        var doc = await ConnectorHttp.SendAsync(client, request, op, ct);

        var root = doc.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown_error";
            doc.Dispose();
            throw new ConnectorOperationException($"{op}: Slack rejected the request ({error}).{Hint(error)}",
                retryable: error is "ratelimited" or "internal_error" or "service_unavailable");
        }

        return doc;
    }

    private static string Hint(string? error) => error switch
    {
        "not_in_channel" => " Invite the Flowamaz app to the channel (/invite @Flowamaz) or use a public channel.",
        "channel_not_found" => " Check the channel name or id in the node inputs.",
        "invalid_auth" or "token_revoked" or "not_authed" or "account_inactive" => " Reconnect Slack in Settings → Credentials.",
        "missing_scope" => " Reconnect Slack so the app is granted the chat:write scope.",
        _ => string.Empty,
    };
}

public sealed class SlackSendMessageHandler : SlackHandlerBase
{
    public SlackSendMessageHandler(IHttpClientFactory f, ILogger<SlackSendMessageHandler> l) : base(f, l) { }
    public override string OperationId => "send-message";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[Slack:send-message] ExecuteAsync entry");
        var root = input.RootElement;
        var channel = ConnectorInput.Required(root, "channel", "slack/send-message");
        var text = ConnectorInput.Required(root, "text", "slack/send-message");

        var body = new Dictionary<string, object> { ["channel"] = channel, ["text"] = text };
        if (root.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            body["blocks"] = blocks.Clone();

        var result = await PostMessageAsync(credential, body, ct);
        Logger.LogInformation("[Slack:send-message] ExecuteAsync exit");
        return result;
    }
}

/// <summary>Slack DM — chat.postMessage with the user id as channel.</summary>
public sealed class SlackSendDmHandler : SlackHandlerBase
{
    public SlackSendDmHandler(IHttpClientFactory f, ILogger<SlackSendDmHandler> l) : base(f, l) { }
    public override string OperationId => "send-dm";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[Slack:send-dm] ExecuteAsync entry");
        var root = input.RootElement;
        var userId = ConnectorInput.Required(root, "user_id", "slack/send-dm");
        var text = ConnectorInput.Required(root, "text", "slack/send-dm");

        var result = await PostMessageAsync(credential, new { channel = userId, text }, ct);
        Logger.LogInformation("[Slack:send-dm] ExecuteAsync exit");
        return result;
    }
}

/// <summary>
/// Posts a Block Kit approval message with Approve/Reject buttons for a human-gate decision.
/// Input: { channel, gate_decision_id, workflow_name, gate_summary }
/// </summary>
public sealed class SlackPostApprovalMessageHandler : SlackHandlerBase
{
    public SlackPostApprovalMessageHandler(IHttpClientFactory f, ILogger<SlackPostApprovalMessageHandler> l) : base(f, l) { }
    public override string OperationId => "post-approval-message";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[Slack:post-approval-message] ExecuteAsync entry");
        var root = input.RootElement;
        var channel = ConnectorInput.Required(root, "channel", "slack/post-approval-message");
        var gateDecisionId = ConnectorInput.Required(root, "gate_decision_id", "slack/post-approval-message");
        var workflowName = ConnectorInput.Optional(root, "workflow_name") ?? string.Empty;
        var gateSummary = ConnectorInput.Optional(root, "gate_summary") ?? string.Empty;

        var result = await PostMessageAsync(credential, new
        {
            channel,
            text = $"Approval required for workflow: {workflowName}",
            blocks = BuildApprovalBlocks(gateDecisionId, workflowName, gateSummary),
        }, ct);

        Logger.LogInformation("[Slack:post-approval-message] ExecuteAsync exit");
        return result;
    }

    private static object[] BuildApprovalBlocks(string gateDecisionId, string workflowName, string gateSummary) =>
    [
        new
        {
            type = "section",
            text = new { type = "mrkdwn", text = $"*Workflow Approval Required*\n*Workflow:* {workflowName}\n*Summary:* {gateSummary}" }
        },
        new
        {
            type = "actions",
            elements = new object[]
            {
                new { type = "button", text = new { type = "plain_text", text = "Approve" }, style = "primary", value = gateDecisionId, action_id = "approve_gate" },
                new { type = "button", text = new { type = "plain_text", text = "Reject" }, style = "danger", value = gateDecisionId, action_id = "reject_gate" },
            }
        }
    ];
}
