using System.Text.Json;
using Flowamaz.Application.Email;
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
    private readonly IConnectorCredentialResolver? _credentials;
    private readonly Connectors.Services.ConnectorDispatcher? _dispatcher;
    private readonly IConfiguration _config;
    private readonly ILogger<HumanGateNodeWorker> _logger;

    public HumanGateNodeWorker(
        IGateDecisionRepository gateRepo,
        IUnitOfWork unitOfWork,
        IEmailService email,
        IConnectorOperationHandlerRegistry connectorRegistry,
        IConfiguration config,
        ILogger<HumanGateNodeWorker> logger,
        IConnectorCredentialResolver? credentials = null,
        Connectors.Services.ConnectorDispatcher? dispatcher = null)
    {
        _credentials = credentials;
        _dispatcher = dispatcher;
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
            // Idempotent: a redelivered queue message must not open a second approval for the same node.
            var existing = await _gateRepo.GetByNodeAsync(context.InstanceId, context.Node.Id, cancellationToken);
            if (existing is not null && existing.Decision == GateDecisionStatus.Pending)
            {
                _logger.LogInformation("HumanGateNodeWorker: gate already open gate={GateId} — not re-creating", existing.Id);
                return NodeExecutionResult.GateWaiting(existing.Id.ToString());
            }

            var config = context.Node.Config.RootElement;

            // ── Read node config ──────────────────────────────────────────────
            // Spec keys (snake_case) first; canvas-authored gates use assignee/assigneeType/channels/
            // timeoutValue+timeoutUnit, which the engine previously ignored.
            var assigneeType = GetString(config, "assigneeType")?.ToLowerInvariant();
            var assigneeRaw = GetString(config, "assignee");
            var assignedToVariable = GetString(config, "assigned_to_variable")
                ?? (assigneeType == "variable" ? StripPlaceholder(assigneeRaw) : null);
            var directAssigneeEmail = assigneeType == "user" && !string.IsNullOrWhiteSpace(assigneeRaw) ? assigneeRaw.Trim() : null;
            var deliveryChannelRaw = GetString(config, "delivery_channel") ?? ChannelFromCanvas(config) ?? "Portal";
            var gateLabel = GetString(config, "gate_label") ?? (string.IsNullOrWhiteSpace(context.Node.Label) ? "Approval Required" : context.Node.Label);
            var gateSummary = GetString(config, "gate_summary") ?? string.Empty;
            var timeoutHours = GetInt(config, "timeout_hours") ?? TimeoutFromCanvas(config) ?? DefaultTimeoutHours;
            var slackChannel = GetString(config, "slack_channel") ?? GetString(config, "slackChannel");
            var slackCredentialRef = GetString(config, "slack_credential") ?? GetString(config, "slackCredential");
            var teamsWebhookUrl = GetString(config, "teams_webhook_url") ?? GetString(config, "teamsWebhookUrl");
            var onTimeout = GateTimeoutActions.Normalize(GetString(config, "on_timeout") ?? GetString(config, "onTimeout"));
            var escalateTo = GetString(config, "escalate_to") ?? GetString(config, "escalateTo");
            var workflowName = GetString(config, "workflow_name") ?? "Workflow";

            // ── Resolve assignee email ────────────────────────────────────────
            string? assigneeEmail = directAssigneeEmail;
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
                OnTimeout = onTimeout,
                EscalateToEmail = string.IsNullOrWhiteSpace(escalateTo) ? null : escalateTo.Trim(),
                TimeoutHours = timeoutHours,
            };

            await _gateRepo.AddAsync(gate, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "HumanGateNodeWorker: created gate={GateId} channel={Channel} assignee={Assignee}",
                gate.Id, deliveryChannel, assigneeEmail ?? "(none)");

            // ── Deliver notification ──────────────────────────────────────────
            await DeliverAsync(gate, deliveryChannel, gateLabel, gateSummary, workflowName, slackChannel, slackCredentialRef, teamsWebhookUrl, cancellationToken);

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
        string? slackCredentialRef,
        string? teamsWebhookUrl,
        CancellationToken ct)
    {
        switch (channel)
        {
            case GateDeliveryChannel.Slack:
                await DeliverSlackAsync(gate, gateLabel, gateSummary, workflowName, slackChannel, slackCredentialRef, ct);
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
        string? slackCredentialRef,
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
            var credential = _credentials is null
                ? null
                : await _credentials.ResolveAsync(gate.WorkspaceId, "slack", slackCredentialRef, ct);
            if (credential is null)
            {
                _logger.LogWarning(
                    "HumanGateNodeWorker: no Slack credential in workspace — gate={GateId} is available in the portal only", gate.Id);
                return;
            }

            await RunAsync(handler, "slack", "post-approval-message", inputPayload, credential, gate, ct);
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
            await RunAsync(handler, "microsoft-teams", "post-adaptive-card", inputPayload, null, gate, ct);
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

        var expiryHours = gate.ExpiresAt.HasValue
            ? (int)Math.Max(1, Math.Ceiling(gate.ExpiresAt.Value.Subtract(DateTime.UtcNow).TotalHours))
            : DefaultTimeoutHours;

        var (subject, html, text) = EmailTemplateService.GateApproval(
            workflowName, gateSummary, approveUrl, rejectUrl, expiryHours, platformUrl);

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

    /// <summary>Approval delivery goes through the sandbox runner when one is configured.</summary>
    private async Task RunAsync(IConnectorOperationHandler handler, string connectorId, string operationId,
        JsonDocument input, Core.Models.ConnectorCredential? credential, GateDecision gate, CancellationToken ct)
    {
        if (_dispatcher is not null && _dispatcher.IsSandboxed(connectorId))
        {
            using var _ = await _dispatcher.ExecuteAsync(connectorId, operationId, input, credential,
                gate.WorkspaceId, gate.InstanceId, gate.NodeId, TimeSpan.FromSeconds(30), ct);
            return;
        }
        using var __ = await handler.ExecuteAsync(input, credential, ct);
    }

    /// <summary>"${approver}" / "{{ variables.approver }}" / "approver" → "approver".</summary>
    internal static string? StripPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.StartsWith("${", StringComparison.Ordinal) && v.EndsWith('}')) v = v[2..^1];
        else if (v.StartsWith("{{", StringComparison.Ordinal) && v.EndsWith("}}", StringComparison.Ordinal)) v = v[2..^2];
        v = v.Trim();
        if (v.StartsWith("variables.", StringComparison.Ordinal)) v = v["variables.".Length..];
        return v.Length == 0 ? null : v;
    }

    /// <summary>Canvas stores channels as { email: bool, slack: bool, teams: bool }; pick the richest enabled.</summary>
    private static string? ChannelFromCanvas(JsonElement config)
    {
        if (!config.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Object) return null;
        bool On(string key) => channels.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        return On("slack") ? "Slack" : On("teams") ? "Teams" : On("email") ? "Email" : null;
    }

    private static int? TimeoutFromCanvas(JsonElement config)
    {
        var value = GetInt(config, "timeoutValue");
        if (value is null or <= 0) return null;
        return string.Equals(GetString(config, "timeoutUnit"), "days", StringComparison.OrdinalIgnoreCase) ? value * 24 : value;
    }

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
