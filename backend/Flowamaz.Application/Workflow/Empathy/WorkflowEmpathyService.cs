using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Empathy;

/// <summary>
/// Deterministic graph-walk empathy analyser. No AI calls — pure structural analysis.
/// </summary>
public sealed class WorkflowEmpathyService : IWorkflowEmpathyService
{
    // connectorIds that send email or messaging notifications
    private static readonly HashSet<string> EmailConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "email-smtp", "microsoft-365", "sendgrid" };

    private static readonly HashSet<string> MessagingConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "slack", "teams", "discord" };

    // 72-hour timeout in seconds
    private const int LongWaitThresholdSeconds = 72 * 3600;

    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly SfgParser _sfgParser;
    private readonly ILogger<WorkflowEmpathyService> _log;

    public WorkflowEmpathyService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        SfgParser sfgParser,
        ILogger<WorkflowEmpathyService> log)
    {
        _definitions = definitions;
        _metrics = metrics;
        _sfgParser = sfgParser;
        _log = log;
    }

    public async Task<EmpathyAnalysis> AnalyseAsync(
        Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default)
    {
        _log.LogInformation(
            "WorkflowEmpathyService.AnalyseAsync entry id={Id} workspaceId={WorkspaceId}",
            workflowDefinitionId, workspaceId);

        try
        {
            var def = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, ct);
            if (def is null)
            {
                throw new InvalidOperationException(
                    $"Workflow {workflowDefinitionId} not found in workspace {workspaceId}. " +
                    "Verify the workflow ID and your workspace access.");
            }

            WorkflowGraph graph;
            try
            {
                graph = await _sfgParser.ParseAsync(def.YamlContent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "WorkflowEmpathyService.AnalyseAsync parse failed id={Id}", workflowDefinitionId);
                // Return a zero-score analysis rather than throwing — the YAML is malformed
                return new EmpathyAnalysis(
                    workflowDefinitionId, 0, 0, 0, 0, 0, 0,
                    new[] { new EmpathyIssue("unknown", "Unparseable workflow", EmpathyIssueType.NoOutcomeNotification,
                        "The workflow YAML could not be parsed so no empathy analysis is available.",
                        "Fix the YAML syntax errors and re-analyse.", "High") });
            }

            // ── Metric-based avg duration ─────────────────────────────────────────
            var since = DateTime.UtcNow.AddDays(-30);
            var metricRows = await _metrics.GetForDefinitionSinceAsync(workflowDefinitionId, workspaceId, since, ct);
            double avgDays = 0;
            if (metricRows.Count > 0)
            {
                var totalMs = metricRows.Sum(m => (double)m.AvgDurationMs);
                avgDays = Math.Round(totalMs / metricRows.Count / 86_400_000.0, 2);
            }

            // ── Graph walk ────────────────────────────────────────────────────────
            int emailsSent = 0;
            int waitPeriods = 0;
            int statusUpdates = 0;
            bool hasLongWait = false;

            foreach (var node in graph.Nodes)
            {
                if (node.Type == NodeType.HumanGate)
                {
                    waitPeriods++;
                    if (node.TimeoutPolicy is not null && node.TimeoutPolicy.TimeoutSeconds > LongWaitThresholdSeconds)
                    {
                        hasLongWait = true;
                    }
                }

                if (node.Type == NodeType.Action)
                {
                    var connectorId = GetStringProperty(node, "connector_id");
                    var operationId = GetStringProperty(node, "operation_id");

                    if (connectorId is not null)
                    {
                        if (EmailConnectors.Contains(connectorId))
                        {
                            emailsSent++;
                        }

                        if (MessagingConnectors.Contains(connectorId))
                        {
                            statusUpdates++;
                        }

                        // Email-based status updates (non-smtp email operations also count)
                        if (EmailConnectors.Contains(connectorId) &&
                            operationId is not null &&
                            (operationId.Contains("send", StringComparison.OrdinalIgnoreCase) ||
                             operationId.Contains("email", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Already counted in emailsSent; messaging was already added above
                        }
                    }
                }
            }

            // Treat email nodes also as status updates when workflow has human gates
            // (requester gets an email when gate resolves = status update)
            if (waitPeriods > 0)
            {
                statusUpdates += emailsSent;
            }

            // ── Issue detection ───────────────────────────────────────────────────
            var issues = new List<EmpathyIssue>();

            // NoOutcomeNotification: nobody is ever notified of the outcome
            if (emailsSent == 0 && statusUpdates == 0)
            {
                issues.Add(new EmpathyIssue(
                    "workflow",
                    "No outcome notification",
                    EmpathyIssueType.NoOutcomeNotification,
                    "The workflow never sends an email or message to notify the requester of the outcome.",
                    "Add an Action node (email-smtp or slack) near the End node to inform the requester when the workflow completes.",
                    "High"));
            }

            // VisibilityGap: requester waits but never receives updates
            if (waitPeriods > 0 && statusUpdates == 0)
            {
                issues.Add(new EmpathyIssue(
                    "workflow",
                    "Visibility gap during wait",
                    EmpathyIssueType.VisibilityGap,
                    "The requester is left in the dark while waiting — no status notifications are sent during human-gate wait periods.",
                    "Add a notification action (email or Slack) immediately before and after each human-gate to keep the requester informed.",
                    "High"));
            }

            // MultipleEmails: more than 3 email nodes is overwhelming
            if (emailsSent > 3)
            {
                issues.Add(new EmpathyIssue(
                    "workflow",
                    "Too many emails",
                    EmpathyIssueType.MultipleEmails,
                    $"The workflow sends {emailsSent} emails. Sending more than 3 emails for a single workflow risks overwhelming the requester.",
                    "Consolidate status emails into a single summary notification where possible, or use a digest approach.",
                    "Medium"));
            }

            // LongWait: any gate with timeout > 72 h
            if (hasLongWait)
            {
                var longGate = graph.Nodes
                    .Where(n => n.Type == NodeType.HumanGate &&
                                n.TimeoutPolicy?.TimeoutSeconds > LongWaitThresholdSeconds)
                    .First();

                issues.Add(new EmpathyIssue(
                    longGate.Id,
                    longGate.Label,
                    EmpathyIssueType.LongWait,
                    $"The gate '{longGate.Label}' has a timeout longer than 72 hours. Requesters can feel forgotten after more than three days with no progress.",
                    "Add an automatic reminder notification after 24 h of inactivity, or break the gate into smaller checkpoints.",
                    "Medium"));
            }

            // ── Score ─────────────────────────────────────────────────────────────
            int score = 100;
            foreach (var issue in issues)
            {
                score -= issue.Type switch
                {
                    EmpathyIssueType.VisibilityGap => 10,
                    EmpathyIssueType.NoOutcomeNotification => 15,
                    EmpathyIssueType.LongWait => 10,
                    EmpathyIssueType.MultipleEmails => 5 * (emailsSent - 3),
                    _ => 5,
                };
            }
            score = Math.Max(0, score);

            int visibilityGapHours = (waitPeriods > 0 && statusUpdates == 0) ? 72 : 0;

            var analysis = new EmpathyAnalysis(
                workflowDefinitionId,
                emailsSent,
                waitPeriods,
                statusUpdates,
                avgDays,
                visibilityGapHours,
                score,
                issues.AsReadOnly());

            _log.LogInformation(
                "WorkflowEmpathyService.AnalyseAsync exit id={Id} score={Score} issues={IssueCount}",
                workflowDefinitionId, score, issues.Count);

            return analysis;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _log.LogError(ex, "WorkflowEmpathyService.AnalyseAsync error id={Id}", workflowDefinitionId);
            throw;
        }
    }

    private static string? GetStringProperty(SfgNode node, string propertyName)
    {
        if (node.Config.RootElement.TryGetProperty(propertyName, out var val))
        {
            return val.GetString();
        }
        return null;
    }
}
