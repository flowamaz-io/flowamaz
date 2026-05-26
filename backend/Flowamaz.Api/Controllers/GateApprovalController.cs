using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Gates;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Handles no-auth email click-through links (approve/reject via signed URLs) and Slack
/// Interactive Actions for human-gate decisions. Both paths call <see cref="GateService"/>
/// to record the decision; the orchestrator resumes the instance after commit.
/// </summary>
[ApiController]
[Route("api/v1/gates")]
[AllowAnonymous]
public sealed class GateApprovalController : ControllerBase
{
    private readonly GateService _gates;
    private readonly ICurrentUserService _currentUser;
    private readonly IConfiguration _config;
    private readonly ILogger<GateApprovalController> _logger;

    public GateApprovalController(
        GateService gates,
        ICurrentUserService currentUser,
        IConfiguration config,
        ILogger<GateApprovalController> logger)
    {
        _gates = gates;
        _currentUser = currentUser;
        _config = config;
        _logger = logger;
    }

    // ── Email click-through: Approve ──────────────────────────────────────────

    [HttpGet("{gateDecisionId:guid}/approve")]
    public async Task<ContentResult> ApproveViaEmail(
        Guid gateDecisionId, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GateApprovalController.ApproveViaEmail entry gate={GateId}", gateDecisionId);

        var result = await HandleEmailDecision(gateDecisionId, "approve", sig, "approved", cancellationToken);

        _logger.LogInformation(
            "GateApprovalController.ApproveViaEmail exit gate={GateId} valid={Valid}", gateDecisionId, result.valid);

        return result.valid
            ? HtmlPage("Decision Recorded", "Your approval has been recorded. The workflow will resume shortly.", "#16a34a")
            : HtmlPage("Link Invalid or Expired", "This approval link is invalid or has expired. Please use the portal to approve this request.", "#dc2626", statusCode: 400);
    }

    // ── Email click-through: Reject ───────────────────────────────────────────

    [HttpGet("{gateDecisionId:guid}/reject")]
    public async Task<ContentResult> RejectViaEmail(
        Guid gateDecisionId, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GateApprovalController.RejectViaEmail entry gate={GateId}", gateDecisionId);

        var result = await HandleEmailDecision(gateDecisionId, "reject", sig, "rejected", cancellationToken);

        _logger.LogInformation(
            "GateApprovalController.RejectViaEmail exit gate={GateId} valid={Valid}", gateDecisionId, result.valid);

        return result.valid
            ? HtmlPage("Decision Recorded", "Your rejection has been recorded.", "#64748b")
            : HtmlPage("Link Invalid or Expired", "This rejection link is invalid or has expired. Please use the portal to reject this request.", "#dc2626", statusCode: 400);
    }

    // ── Slack Interactive Actions ─────────────────────────────────────────────

    [HttpPost("/api/v1/integrations/slack/actions")]
    public async Task<IActionResult> SlackActions(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GateApprovalController.SlackActions entry");

        // Validate Slack signing secret.
        if (!await ValidateSlackSignatureAsync())
        {
            _logger.LogWarning("GateApprovalController.SlackActions invalid Slack signature");
            return Unauthorized();
        }

        // Slack sends actions as url-encoded 'payload' field.
        string payloadJson;
        if (Request.ContentType?.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
        {
            payloadJson = Request.Form["payload"].ToString();
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            payloadJson = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BadRequest(new { error = "Empty Slack payload." });
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(payloadJson).RootElement;
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON in Slack payload." });
        }

        // Extract action.
        if (!root.TryGetProperty("actions", out var actions) || actions.GetArrayLength() == 0)
        {
            return Ok(new { text = "No actions found." });
        }

        var action = actions.EnumerateArray().First();
        var actionId = action.TryGetProperty("action_id", out var aidProp) ? aidProp.GetString() : null;
        var value = action.TryGetProperty("value", out var valProp) ? valProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(value))
        {
            return Ok(new { text = "Unrecognised action format." });
        }

        var decision = actionId switch
        {
            "approve_gate" => "approved",
            "reject_gate" => "rejected",
            _ => null
        };

        if (decision is null)
        {
            return Ok(new { text = "Unrecognised action_id." });
        }

        if (!Guid.TryParse(value, out var gateId))
        {
            return Ok(new { text = "Invalid gate ID." });
        }

        // Look up gate by ID to get instanceId and nodeId.
        var gate = await _gates.GetByGateIdAsync(gateId, cancellationToken);
        if (gate is null)
        {
            _logger.LogWarning("GateApprovalController.SlackActions gate not found id={GateId}", gateId);
            return Ok(new { text = "Gate not found or already decided." });
        }

        try
        {
            await _gates.DecideAsync(
                gate.WorkspaceId, gate.InstanceId, gate.NodeId,
                decision, null, Guid.Empty, cancellationToken);

            _logger.LogInformation(
                "GateApprovalController.SlackActions exit gate={GateId} decision={Decision}", gateId, decision);

            return Ok(new { text = $"Decision recorded: {decision}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GateApprovalController.SlackActions error gate={GateId}", gateId);
            return Ok(new { text = "Failed to record decision — please use the portal." });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(bool valid, GateInfoDto? gate)> HandleEmailDecision(
        Guid gateDecisionId, string action, string sig, string decision, CancellationToken ct)
    {
        var gate = await _gates.GetByGateIdAsync(gateDecisionId, ct);
        if (gate is null) return (false, null);

        // Verify HMAC signature.
        var signingKey = _config["GATE_SIGNING_KEY"] ?? "default-dev-key";
        var expiresUnix = gate.ExpiresAt.HasValue
            ? new DateTimeOffset(gate.ExpiresAt.Value).ToUnixTimeSeconds()
            : 0L;

        var message = $"{gateDecisionId}:{action}:{expiresUnix}";
        if (!GateHmacHelper.ValidateHmac(message, sig, signingKey))
        {
            _logger.LogWarning(
                "GateApprovalController: HMAC validation failed for gate={GateId} action={Action}",
                gateDecisionId, action);
            return (false, null);
        }

        // Check expiry.
        if (gate.ExpiresAt.HasValue && gate.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning(
                "GateApprovalController: expired gate link gate={GateId} expiresAt={ExpiresAt}",
                gateDecisionId, gate.ExpiresAt);
            return (false, null);
        }

        try
        {
            await _gates.DecideAsync(
                gate.WorkspaceId, gate.InstanceId, gate.NodeId,
                decision, "Decided via email link.", Guid.Empty, ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GateApprovalController: decision recording failed gate={GateId}", gateDecisionId);
            return (false, null);
        }
    }

    private async Task<bool> ValidateSlackSignatureAsync()
    {
        var slackSigningSecret = _config["SLACK_SIGNING_SECRET"];
        if (string.IsNullOrWhiteSpace(slackSigningSecret))
        {
            // In development, skip validation if no secret configured.
            _logger.LogWarning("GateApprovalController: SLACK_SIGNING_SECRET not configured — skipping signature validation");
            return true;
        }

        if (!Request.Headers.TryGetValue("X-Slack-Request-Timestamp", out var timestampValues)
            || !Request.Headers.TryGetValue("X-Slack-Signature", out var signatureValues))
        {
            return false;
        }

        var timestamp = timestampValues.ToString();
        var signature = signatureValues.ToString();

        // Replay attack protection: reject requests older than 5 minutes.
        if (long.TryParse(timestamp, out var ts))
        {
            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts;
            if (age > 300) return false;
        }

        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var sigBase = $"v0:{timestamp}:{body}";
        var keyBytes = Encoding.UTF8.GetBytes(slackSigningSecret);
        var msgBytes = Encoding.UTF8.GetBytes(sigBase);
        var hash = HMACSHA256.HashData(keyBytes, msgBytes);
        var computed = $"v0={Convert.ToHexString(hash).ToLowerInvariant()}";

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signature));
    }

    private ContentResult HtmlPage(string title, string message, string color, int statusCode = 200)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{System.Net.WebUtility.HtmlEncode(title)}</title></head>
            <body style="font-family:sans-serif;max-width:480px;margin:80px auto;text-align:center;padding:24px">
              <div style="font-size:48px;margin-bottom:16px">{(color == "#16a34a" ? "✓" : color == "#64748b" ? "✗" : "⚠")}</div>
              <h1 style="color:{color};font-size:24px">{System.Net.WebUtility.HtmlEncode(title)}</h1>
              <p style="color:#475569;font-size:16px">{System.Net.WebUtility.HtmlEncode(message)}</p>
              <a href="/" style="color:#6366f1;font-size:14px">Go to Flowamaz Portal →</a>
            </body>
            </html>
            """;

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode,
        };
    }
}

