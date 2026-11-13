using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Empathy;

/// <summary>
/// Empathy View (FUNCTIONAL.md §8.2): what the workflow is like for the person who started it. Two deterministic
/// layers, no AI calls:
/// <list type="bullet">
/// <item><b>Structure</b> — a graph walk: where it sends messages, where it waits on people, whether anyone hears the
/// outcome.</item>
/// <item><b>Real runs</b> — finished, non-test runs of the last 30 days: days to outcome, messages actually sent,
/// the longest stretch without any message, time spent waiting on approvals, whether the outcome was announced.
/// Findings quote those numbers ("Runs send 7 messages over 3 days…").</item>
/// </list>
/// </summary>
public sealed class WorkflowEmpathyService : IWorkflowEmpathyService
{
    // connectorIds that send email or messaging notifications
    // Ids from OfficialConnectorCatalog (plus older aliases still found in saved YAML).
    private static readonly HashSet<string> EmailConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "email-smtp", "email", "microsoft-365", "sendgrid" };

    private static readonly HashSet<string> MessagingConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "slack", "microsoft-teams", "teams", "discord" };

    private const int RunWindowDays = 30;
    private const int MaxRunsAnalysed = 200;

    // 72-hour timeout in seconds
    private const int LongWaitThresholdSeconds = 72 * 3600;

    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly SfgParser _sfgParser;
    private readonly ILogger<WorkflowEmpathyService> _log;
    private readonly IWorkflowEventRepository? _events;

    public WorkflowEmpathyService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        SfgParser sfgParser,
        ILogger<WorkflowEmpathyService> log,
        IWorkflowEventRepository? events = null)
    {
        _events = events;
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

        var def = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, ct);
        if (def is null)
        {
            throw new InvalidOperationException(
                $"Workflow {workflowDefinitionId} not found in workspace {workspaceId}. " +
                "Verify the workflow ID and your workspace access.");
        }

        return await AnalyseYamlAsync(def.YamlContent, workflowDefinitionId, workspaceId, ct);
    }

    public async Task<EmpathyAnalysis> AnalyseYamlAsync(
        string yaml, Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default)
    {
        _log.LogInformation(
            "WorkflowEmpathyService.AnalyseYamlAsync entry id={Id} workspaceId={WorkspaceId}",
            workflowDefinitionId, workspaceId);

        try
        {
            WorkflowGraph graph;
            try
            {
                graph = await _sfgParser.ParseAsync(yaml, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "WorkflowEmpathyService.AnalyseYamlAsync parse failed id={Id}", workflowDefinitionId);
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
                    var connectorId = GetStringProperty(node, "connector_id") ?? GetStringProperty(node, "connector");
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

            // ── Real runs ─────────────────────────────────────────────────────────
            var evidence = await RunEvidenceAsync(graph, workflowDefinitionId, workspaceId, ct);
            if (evidence is not null)
            {
                var runIssues = RunIssues(evidence, graph);
                issues.AddRange(runIssues);
                score = Math.Max(0, score - runIssues.Sum(i => i.Severity == "High" ? 10 : 5));
                avgDays = evidence.AvgDaysToOutcome;
                visibilityGapHours = (int)Math.Round(evidence.MedianLongestSilenceHours);
            }

            var analysis = new EmpathyAnalysis(
                workflowDefinitionId,
                emailsSent,
                waitPeriods,
                statusUpdates,
                avgDays,
                visibilityGapHours,
                score,
                issues.AsReadOnly(),
                evidence);

            _log.LogInformation(
                "WorkflowEmpathyService.AnalyseYamlAsync exit id={Id} score={Score} issues={IssueCount}",
                workflowDefinitionId, score, issues.Count);

            return analysis;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "WorkflowEmpathyService.AnalyseYamlAsync error id={Id}", workflowDefinitionId);
            throw;
        }
    }

    /// <summary>Measures finished runs; null when there are none (or no event store).</summary>
    private async Task<EmpathyRunEvidence?> RunEvidenceAsync(WorkflowGraph graph, Guid definitionId, Guid workspaceId, CancellationToken ct)
    {
        if (_events is null || definitionId == Guid.Empty) return null;
        var notify = NotificationNodeIds(graph);
        var runs = await _events.GetRunTimelinesAsync(workspaceId, definitionId, DateTime.UtcNow.AddDays(-RunWindowDays), MaxRunsAnalysed, notify, ct);
        return Measure(runs, notify);
    }

    /// <summary>Action nodes that send an email or a chat message.</summary>
    internal static HashSet<string> NotificationNodeIds(WorkflowGraph graph) =>
        graph.Nodes
            .Where(n => n.Type == NodeType.Action &&
                        (GetStringProperty(n, "connector_id") ?? GetStringProperty(n, "connector")) is { } c &&
                        (EmailConnectors.Contains(c) || MessagingConnectors.Contains(c)))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The requester's experience across these runs. Pure, so it is unit-tested directly.</summary>
    internal static EmpathyRunEvidence? Measure(IReadOnlyList<EmpathyRunTimeline> runs, IReadOnlySet<string> notificationNodes)
    {
        var usable = runs.Where(r => r.EndedAt > r.StartedAt).ToList();
        if (usable.Count == 0) return null;

        var days = new List<double>();
        var messages = new List<int>();
        var silences = new List<double>();
        var waitingShares = new List<double>();
        var notifiedRuns = 0;
        var gateWaits = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        foreach (var run in usable)
        {
            var duration = run.EndedAt - run.StartedAt;
            days.Add(duration.TotalDays);

            var sent = run.CompletedNodes.Where(n => notificationNodes.Contains(n.NodeId)).Select(n => n.At).OrderBy(t => t).ToList();
            messages.Add(sent.Count);

            // Longest quiet stretch: start → first message → … → last message → outcome.
            var points = new List<DateTime> { run.StartedAt };
            points.AddRange(sent.Where(t => t > run.StartedAt && t < run.EndedAt));
            points.Add(run.EndedAt);
            silences.Add(points.Zip(points.Skip(1), (a, b) => (b - a).TotalHours).Max());

            double waited = 0;
            foreach (var gate in run.Gates)
            {
                var until = gate.DecidedAt is { } d && d < run.EndedAt ? d : run.EndedAt; // a run ends its waits
                if (until <= gate.OpenedAt) continue;
                var hours = (until - gate.OpenedAt).TotalHours;
                waited += hours;
                if (!gateWaits.TryGetValue(gate.NodeId, out var list)) gateWaits[gate.NodeId] = list = [];
                list.Add(hours);
            }
            waitingShares.Add(Math.Min(1, waited / Math.Max(duration.TotalHours, 1e-9)));

            var lastDecision = run.Gates.Where(g => g.DecidedAt is not null).Select(g => g.DecidedAt!.Value).DefaultIfEmpty(run.StartedAt).Max();
            if (sent.Any(t => t >= lastDecision)) notifiedRuns++;
        }

        var slowest = gateWaits.Select(kv => (kv.Key, Avg: kv.Value.Average())).OrderByDescending(g => g.Avg).FirstOrDefault();
        silences.Sort();
        return new EmpathyRunEvidence(
            usable.Count,
            Math.Round(days.Average(), 2),
            Math.Round(messages.Average(), 1),
            Math.Round(silences[silences.Count / 2], 1),
            (int)Math.Round(waitingShares.Average() * 100),
            (int)Math.Round(notifiedRuns * 100.0 / usable.Count),
            slowest.Key,
            Math.Round(slowest.Avg, 1));
    }

    /// <summary>Findings backed by run numbers. Each says what was measured and what to change.</summary>
    internal static List<EmpathyIssue> RunIssues(EmpathyRunEvidence e, WorkflowGraph graph)
    {
        var issues = new List<EmpathyIssue>();
        string Runs() => e.RunsAnalysed == 1 ? "the one finished run" : $"the last {e.RunsAnalysed} finished runs";
        string Days(double d) => d >= 1 ? $"{d:0.#} days" : $"{d * 24:0.#} hours";

        if (e.AvgMessagesPerRun >= 5)
            issues.Add(new EmpathyIssue("workflow", "Too many messages", EmpathyIssueType.MultipleEmails,
                $"Across {Runs()}, the workflow sent {e.AvgMessagesPerRun:0.#} messages per run over {Days(e.AvgDaysToOutcome)} on average.",
                "Combine the progress messages into one update per stage, and send the rest only to the people who must act.",
                "Medium"));

        if (e.MedianLongestSilenceHours >= 24)
            issues.Add(new EmpathyIssue("workflow", "Long silence", EmpathyIssueType.VisibilityGap,
                $"In a typical run the requester heard nothing for {e.MedianLongestSilenceHours:0.#} hours in a row ({Runs()}).",
                "Send a short status update when each approval step opens (for example: 'With Finance for approval, usually decided within a day').",
                e.MedianLongestSilenceHours >= 72 ? "High" : "Medium"));

        if (e.PercentRunsOutcomeNotified < 50)
            issues.Add(new EmpathyIssue("workflow", "Outcome rarely announced", EmpathyIssueType.NoOutcomeNotification,
                $"Only {e.PercentRunsOutcomeNotified}% of {Runs()} sent any message after the final decision, so most requesters had to find out the outcome themselves.",
                "Add an email or chat message after the last approval, on both the approved and rejected paths.",
                "High"));

        if (e.SlowestGateNodeId is { } gateId && e.PercentTimeWaitingOnApprovals >= 50 && e.SlowestGateAvgHours >= 8)
        {
            var label = graph.Nodes.FirstOrDefault(n => n.Id == gateId)?.Label ?? gateId;
            issues.Add(new EmpathyIssue(gateId, label, EmpathyIssueType.LongWait,
                $"Runs spent {e.PercentTimeWaitingOnApprovals}% of their time waiting for approvals; '{label}' took {e.SlowestGateAvgHours:0.#} hours on average.",
                "Set a timeout with a reminder or escalation on this step, or add a backup approver.",
                e.SlowestGateAvgHours >= 48 ? "High" : "Medium"));
        }
        return issues;
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
