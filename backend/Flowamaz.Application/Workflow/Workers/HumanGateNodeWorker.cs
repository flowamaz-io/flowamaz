using System.Text.Json;
using Flowamaz.Application.Workflow.Gates;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Executes HumanGate nodes. Creates a <see cref="GateDecision"/>, delivers the request via the
/// configured channel (Portal / Slack / Teams / Email), then returns
/// <see cref="NodeExecutionResult.GateWaiting"/> so the orchestrator parks the instance until
/// the assignee approves or rejects.
/// </summary>
public sealed class HumanGateNodeWorker : INodeWorker
{
    private const int DefaultTimeoutHours = 48;

    private readonly IGateDecisionRepository _gateRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _email;
    private readonly IConnectorOperationHandlerRegistry _connectorRegistry;
    private readonly IConfiguration _config;
    private readonly ILogger<HumanGateNodeWorker> _logger;

    public HumanGateNodeWorker(
        IGateDecisionRepository gateRepo,
        IUnitOfWork unitOfWork,
        IEmailService email,
        IConnectorOperationHandlerRegistry connectorRegistry,
        IConfiguration config,
        ILogger<HumanGateNodeWorker> logger)
    {
        _gateRepo = gateRepo;
        _unitOfWork = unitOfWork;
        _email = email;
        _connectorRegistry = connectorRegistry;
        _config = config;
        _logger = logger;
    }

    public NodeType SupportedType => NodeType.HumanGate;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "HumanGateNodeWorker.ExecuteAsync enter instance={InstanceId} node={NodeId}",
            context.InstanceId, context.Node.Id);

        try
        {
            var config = context.Node.Config.RootElement;

            // ── Read node config ──────────────────────────────────────────────
            var assignedToVariable = GetString(config, "assigned_to_variable");
            var deliveryChannelRaw = GetString(config, "delivery_channel") ?? "Portal";
            var gateLabel = GetString(config, "gate_label") ?? "Approval Required";
            var gateSummary = GetString(config, "gate_summary") ?? string.Empty;
            var timeoutHours = GetInt(config, "timeout_hours") ?? DefaultTimeoutHours;
            var slackChannel = GetString(config, "slack_channel");
            var teamsWebhookUrl = GetString(config, "teams_webhook_url");
            var workflowName = GetString(config, "workflow_name") ?? "Workflow";

            // ── Resolve assignee email ────────────────────────────────────────
            string? assigneeEmail = null;
            if (!string.IsNullOrWhiteSpace(assignedToVariable)
                && context.Variables.TryGetValue(assignedToVariable, out var emailElement)
                && emailElement.ValueKind == JsonValueKind.String)
            {
                assigneeEmail = emailElement.GetString();
            }

            // ── Parse delivery channel ────────────────────────────────────────
            var deliveryChannel = Enum.TryParse<GateDeliveryChannel>(deliveryChannelRaw, ignoreCase: true, out var parsed)
                ? parsed
                : GateDeliveryChannel.Portal;

            var expiresAt = DateTime.UtcNow.AddHours(timeoutHours);

            // ── Create GateDecision ───────────────────────────────────────────
            var gate = new GateDecision
            {
                Id = Guid.NewGuid(),
                WorkspaceId = context.WorkspaceId,
                InstanceId = context.InstanceId,
                NodeId = context.Node.Id,
                AssignedToEmail = assigneeEmail,
                Decision = GateDecisionStatus.Pending,
                DeliveryChannel = deliveryChannel,
                DeliveryStatus = GateDeliveryStatus.Pending,
                ExpiresAt = expiresAt,
            };

            await _gateRepo.AddAsync(gate, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "HumanGateNodeWorker: created gate={GateId} channel={Channel} assignee={Assignee}",
                gate.Id, deliveryChannel, assigneeEmail ?? "(none)");

            // ── Deliver notification ──────────────────────────────────────────
            await DeliverAsync(gate, deliveryChannel, gateLabel, gateSummary, workflowName, slackChannel, teamsWebhookUrl, cancellationToken);

            _logger.LogInformation(
                "HumanGateNodeWorker.ExecuteAsync exit instance={InstanceId} node={NodeId} gate={GateId}",
                context.InstanceId, context.Node.Id, gate.Id);

            return NodeExecutionResult.GateWaiting(gate.Id.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "HumanGateNodeWorker.ExecuteAsync error instance={InstanceId} node={NodeId}",
                context.InstanceId, context.Node.Id);
            return NodeExecutionResult.Fatal(
                $"Failed to create human gate for node '{context.Node.Id}': {ex.Message}. " +
                "Check the gate node configuration and database connectivity.");
        }
    }

    // ── Delivery ──────────────────────────────────────────────────────────────

    private async Task DeliverAsync(
        GateDecision gate,
        GateDeliveryChannel channel,
        string gateLabel,
        string gateSummary,
        string workflowName,
        string? slackChannel,
        string? teamsWebhookUrl,
        CancellationToken ct)
    {
        switch (channel)
        {
            case GateDeliveryChannel.Slack:
                await DeliverSlackAsync(gate, gateLabel, gateSummary, workflowName, slackChannel, ct);
                break;

            case GateDeliveryChannel.Teams:
                await DeliverTeamsAsync(gate, gateSummary, teamsWebhookUrl, ct);
                break;

            case GateDeliveryChannel.Email:
                await DeliverEmailAsync(gate, gateLabel, gateSummary, workflowName, ct);
                break;

            case GateDeliveryChannel.Portal:
            default:
                _logger.LogInformation(
                    "HumanGateNodeWorker: Portal delivery — gate={GateId} available in the approval queue",
                    gate.Id);
                break;
        }
    }

    private async Task DeliverSlackAsync(
        GateDecision gate,
        string gateLabel,
        string gateSummary,
        string workflowName,
        string? slackChannel,
        CancellationToken ct)
    {
        var handler = _connectorRegistry.Get("slack", "post-approval-message");
        if (handler is null)
        {
            _logger.LogWarning(
                "HumanGateNodeWorker: Slack approval handler not found — gate={GateId} delivery skipped", gate.Id);
            return;
        }

        var channel = slackChannel ?? "#approvals";
        var inputPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            channel,
            gate_decision_id = gate.Id.ToString(),
            workflow_name = workflowName,
            gate_summary = $"{gateLabel}: {gateSummary}",
        }));

        try
        {
            await handler.ExecuteAsync(inputPayload, null, ct);
            _logger.LogInformation(
                "HumanGateNodeWorker: Slack approval message posted gate={GateId} channel={Channel}", gate.Id, channel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "HumanGateNodeWorker: Slack delivery failed for gate={GateId} — gate is still pending in portal",
                gate.Id);
        }
    }

    private async Task DeliverTeamsAsync(
        GateDecision gate,
        string gateSummary,
        string? teamsWebhookUrl,
        CancellationToken ct)
    {
        var handler = _connectorRegistry.Get("microsoft-teams", "post-adaptive-card");
        if (handler is null)
        {
            _logger.LogWarning(
                "HumanGateNodeWorker: Teams adaptive card handler not found — gate={GateId} delivery skipped", gate.Id);
            return;
        }

        var webhookUrl = teamsWebhookUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning(
                "HumanGateNodeWorker: Teams delivery requires teams_webhook_url in node config — gate={GateId} skipped",
                gate.Id);
            return;
        }

        var platformUrl = _config["PLATFORM_URL"] ?? _config["PlatformUrl"] ?? "https://app.flowamaz.com";
        var inputPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            webhook_url = webhookUrl,
            gate_id = gate.Id.ToString(),
            gate_summary = gateSummary,
            platform_url = platformUrl,
        }));

        try
        {
            await handler.ExecuteAsync(inputPayload, null, ct);
            _logger.LogInformation(
                "HumanGateNodeWorker: Teams adaptive card posted gate={GateId}", gate.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "HumanGateNodeWorker: Teams delivery failed for gate={GateId} — gate is still pending in portal",
                gate.Id);
        }
    }

    private async Task DeliverEmailAsync(
        GateDecision gate,
        string gateLabel,
        string gateSummary,
        string workflowName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gate.AssignedToEmail))
        {
            _logger.LogWarning(
                "HumanGateNodeWorker: Email delivery requires assignee email — gate={GateId} skipped", gate.Id);
            return;
        }

        var platformUrl = _config["PLATFORM_URL"] ?? _config["PlatformUrl"] ?? "https://app.flowamaz.com";
        var signingKey = GateHmacHelper.GetSigningKey(_config, _logger);
        var expiresUnix = new DateTimeOffset(gate.ExpiresAt ?? DateTime.UtcNow.AddHours(DefaultTimeoutHours)).ToUnixTimeSeconds();

        var approveSig = GateHmacHelper.BuildHmac($"{gate.Id}:approve:{expiresUnix}", signingKey);
        var rejectSig = GateHmacHelper.BuildHmac($"{gate.Id}:reject:{expiresUnix}", signingKey);

        var approveUrl = $"{platformUrl}/api/v1/gates/{gate.Id}/approve?sig={approveSig}";
        var rejectUrl = $"{platformUrl}/api/v1/gates/{gate.Id}/reject?sig={rejectSig}";

        var subject = $"Approval Required: {gateLabel} — {workflowName}";
        var html = BuildApprovalEmailHtml(workflowName, gateLabel, gateSummary, approveUrl, rejectUrl);
        var text = $"Approval Required for {workflowName}\n\n{gateLabel}: {gateSummary}\n\nApprove: {approveUrl}\nReject: {rejectUrl}";

        var sent = await _email.SendAsync(gate.AssignedToEmail, subject, html, text, ct);
        if (sent)
        {
            _logger.LogInformation(
                "HumanGateNodeWorker: approval email sent to={Email} gate={GateId}",
                gate.AssignedToEmail, gate.Id);
        }
        else
        {
            _logger.LogWarning(
                "HumanGateNodeWorker: approval email failed to={Email} gate={GateId} — gate is still pending in portal",
                gate.AssignedToEmail, gate.Id);
        }
    }

    private static string BuildApprovalEmailHtml(
        string workflowName, string gateLabel, string gateSummary,
        string approveUrl, string rejectUrl) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Approval Required</title></head>
        <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
          <h2 style="color:#1e293b">Approval Required</h2>
          <p><strong>Workflow:</strong> {HtmlEncode(workflowName)}</p>
          <p><strong>Gate:</strong> {HtmlEncode(gateLabel)}</p>
          {(string.IsNullOrWhiteSpace(gateSummary) ? "" : $"<p><strong>Summary:</strong> {HtmlEncode(gateSummary)}</p>")}
          <div style="margin:32px 0;display:flex;gap:16px">
            <a href="{approveUrl}" style="background:#16a34a;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600">Approve</a>
            <a href="{rejectUrl}" style="background:#dc2626;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600;margin-left:16px">Reject</a>
          </div>
          <p style="color:#64748b;font-size:12px">This link expires at the time specified in the workflow configuration.</p>
        </body>
        </html>
        """;

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var val)
        && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var val)
        && val.ValueKind == JsonValueKind.Number
        && val.TryGetInt32(out var i)
            ? i
            : null;
}
