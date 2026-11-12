using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Application.Workflow.Dna;

/// <summary>How a workflow performed over the last 30 days (from hourly metrics).</summary>
public sealed record WorkflowPerformance(int Runs, int SuccessPercent, int SlaCompliancePercent, double AvgHours);

/// <summary>A pattern a better-performing similar workflow uses and this one doesn't.</summary>
public sealed record InheritanceSuggestion(
    string Kind,
    Guid FromWorkflowId,
    string FromWorkflowName,
    int Similarity,
    /// <summary>Why the sibling counts as better, in numbers.</summary>
    string Evidence,
    string Title,
    string Description,
    /// <summary>True when Flowamaz can add the pattern to this workflow's YAML itself.</summary>
    bool CanApply);

/// <summary>
/// The inheritance report for one workflow. <see cref="Message"/> explains an empty list (not enough runs, no
/// similar workflow doing better, or nothing left to borrow).
/// </summary>
public sealed record InheritanceReport(
    Guid WorkflowId, WorkflowPerformance Performance, IReadOnlyList<InheritanceSuggestion> Suggestions, string? Message);

/// <summary>
/// Workflow DNA inheritance (FUNCTIONAL.md §8.8): "underperforming workflows inherit patterns from high-performing
/// siblings". Deterministic, no AI. A sibling is a workflow in the same workspace with similar DNA (score ≥ 60) and at
/// least <see cref="MinRuns"/> runs that does clearly better: 10+ points more runs succeed, 10+ points better SLA
/// compliance, or it finishes in 70% of the time or less. The patterns compared:
/// <list type="bullet">
/// <item><c>gate-timeout</c> — its approval steps have a timeout / timeout action; this one's don't (can apply).</item>
/// <item><c>action-retry</c> — its actions retry; this one's don't (can apply).</item>
/// <item><c>outcome-notification</c> — it messages someone after an approval; this one doesn't (suggestion only:
/// the recipient and wording are this workflow's own).</item>
/// <item><c>compensation</c> — it undoes completed steps on failure; this one doesn't (suggestion only).</item>
/// </list>
/// Applying edits the YAML through YamlDotNet and saves it like an editor save (a draft change, committed to the
/// workspace's Git history); it is published as usual.
/// </summary>
public sealed class WorkflowInheritanceService
{
    public const int MinRuns = 5;
    private const int MinSimilarity = 60;
    private static readonly HashSet<string> NotifyConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "email-smtp", "email", "microsoft-365", "slack", "microsoft-teams", "teams" };

    private readonly IWorkflowDnaService _dna;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly SfgParser _parser;
    private readonly WorkflowService _workflows;
    private readonly ILogger<WorkflowInheritanceService> _log;

    public WorkflowInheritanceService(
        IWorkflowDnaService dna, IWorkflowDefinitionRepository definitions, IWorkflowMetricRepository metrics,
        SfgParser parser, WorkflowService workflows, ILogger<WorkflowInheritanceService> log)
    {
        _dna = dna;
        _definitions = definitions;
        _metrics = metrics;
        _parser = parser;
        _workflows = workflows;
        _log = log;
    }

    public async Task<InheritanceReport?> GetReportAsync(Guid workspaceId, Guid workflowId, CancellationToken ct = default)
    {
        _log.LogInformation("WorkflowInheritanceService.GetReportAsync entry workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
        try
        {
            var me = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct);
            if (me is null) return null;
            var mine = await PerformanceAsync(workspaceId, workflowId, ct);
            if (mine.Runs < MinRuns)
                return new InheritanceReport(workflowId, mine, [],
                    $"This workflow has {mine.Runs} finished {(mine.Runs == 1 ? "run" : "runs")} in the last 30 days. " +
                    $"After {MinRuns}, it's compared with similar workflows that do better, and their patterns are suggested here.");

            var myGraph = await _parser.ParseAsync(me.YamlContent, ct);
            var myPatterns = Patterns.Of(myGraph);
            var dna = await _dna.ComputeDnaAsync(workflowId, workspaceId, ct);
            var similar = await _dna.FindSimilarAsync(workspaceId, dna, limit: 10, ct);

            var suggestions = new List<InheritanceSuggestion>();
            var anyBetter = false;
            foreach (var sibling in similar.Where(s => s.SimilarityScore >= MinSimilarity))
            {
                var theirs = await PerformanceAsync(workspaceId, sibling.WorkflowDefinitionId, ct);
                if (theirs.Runs < MinRuns || Better(theirs, mine) is not { } evidence) continue;
                anyBetter = true;

                var def = await _definitions.GetByIdForWorkspaceAsync(sibling.WorkflowDefinitionId, workspaceId, ct);
                if (def is null) continue;
                WorkflowGraph graph;
                try { graph = await _parser.ParseAsync(def.YamlContent, ct); }
                catch (SfgParseException) { continue; }
                var their = Patterns.Of(graph);

                void Add(string kind, string title, string description, bool canApply)
                {
                    if (suggestions.Any(s => s.Kind == kind)) return; // best sibling first (list is by similarity)
                    suggestions.Add(new InheritanceSuggestion(kind, def.Id, def.Name, sibling.SimilarityScore, evidence, title, description, canApply));
                }

                if (their.GateTimeout is { } t && myPatterns.GatesWithoutTimeout.Count > 0)
                    Add("gate-timeout", "Add a timeout to approval steps",
                        $"'{def.Name}' gives its approvers {t.Hours} hours{(t.OnTimeout is null ? "" : $", then {t.OnTimeout.Replace('_', ' ')}")}. " +
                        $"Here, {Steps(myPatterns.GatesWithoutTimeout.Count)} can wait forever.", canApply: true);
                if (their.ActionRetry is { } r && myPatterns.ActionsWithoutRetry.Count > 0)
                    Add("action-retry", "Retry failed actions",
                        $"'{def.Name}' retries its actions up to {r.MaxAttempts} times ({r.BackoffSeconds}s apart, growing ×{r.BackoffMultiplier:0.#}). " +
                        $"Here, {Steps(myPatterns.ActionsWithoutRetry.Count, "action")} fail on the first error.", canApply: true);
                if (their.NotifiesAfterApproval && !myPatterns.NotifiesAfterApproval)
                    Add("outcome-notification", "Tell people the outcome",
                        $"'{def.Name}' sends a message after its approval step; this workflow doesn't, so requesters must check for themselves. " +
                        "Add an email or chat step after the approval, on both the approved and rejected paths.", canApply: false);
                if (their.UsesCompensation && !myPatterns.UsesCompensation)
                    Add("compensation", "Undo completed steps when a run fails",
                        $"'{def.Name}' defines compensation, so a failure late in a run cleans up what earlier steps did. " +
                        "Add a compensation block to the steps that change other systems.", canApply: false);
            }

            var message = suggestions.Count > 0 ? null
                : anyBetter ? "Similar workflows that do better use the same patterns this one already has. Look at their timing and approvers instead."
                : "No similar workflow in this workspace does clearly better than this one over the last 30 days.";
            _log.LogInformation("WorkflowInheritanceService.GetReportAsync exit suggestions={Count}", suggestions.Count);
            return new InheritanceReport(workflowId, mine, suggestions, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SfgParseException)
        {
            _log.LogError(ex, "WorkflowInheritanceService.GetReportAsync error workflow={WorkflowId}", workflowId);
            throw;
        }
    }

    /// <summary>
    /// Adds a suggested pattern (<c>gate-timeout</c> or <c>action-retry</c>) from <paramref name="fromWorkflowId"/> to this
    /// workflow and saves it. Returns the new YAML, or null when either workflow isn't in the workspace.
    /// </summary>
    public async Task<string?> ApplyAsync(Guid workspaceId, Guid workflowId, string kind, Guid fromWorkflowId, CancellationToken ct = default)
    {
        _log.LogInformation("WorkflowInheritanceService.ApplyAsync entry workflow={WorkflowId} kind={Kind} from={From}", workflowId, kind, fromWorkflowId);
        try
        {
            var me = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct);
            var from = await _definitions.GetByIdForWorkspaceAsync(fromWorkflowId, workspaceId, ct);
            if (me is null || from is null) return null;

            var their = Patterns.Of(await _parser.ParseAsync(from.YamlContent, ct));
            var mine = Patterns.Of(await _parser.ParseAsync(me.YamlContent, ct));
            string yaml = kind switch
            {
                "gate-timeout" when their.GateTimeout is { } t && mine.GatesWithoutTimeout.Count > 0 =>
                    YamlNodes.Edit(me.YamlContent, mine.GatesWithoutTimeout, node =>
                    {
                        var config = YamlNodes.Child(node, "config");
                        config.Children[new YamlScalarNode("timeout_hours")] = new YamlScalarNode(t.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (t.OnTimeout is not null) config.Children[new YamlScalarNode("on_timeout")] = new YamlScalarNode(t.OnTimeout);
                    }),
                "action-retry" when their.ActionRetry is { } r && mine.ActionsWithoutRetry.Count > 0 =>
                    YamlNodes.Edit(me.YamlContent, mine.ActionsWithoutRetry, node =>
                    {
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        node.Children[new YamlScalarNode("retry")] = new YamlMappingNode(
                            new YamlScalarNode("maxAttempts"), new YamlScalarNode(r.MaxAttempts.ToString(inv)),
                            new YamlScalarNode("backoffSeconds"), new YamlScalarNode(r.BackoffSeconds.ToString(inv)),
                            new YamlScalarNode("backoffMultiplier"), new YamlScalarNode(r.BackoffMultiplier.ToString(inv)));
                    }),
                "gate-timeout" or "action-retry" => throw new InheritanceNotApplicableException(
                    $"'{from.Name}' no longer has this pattern, or this workflow already has it everywhere. Refresh the suggestions."),
                _ => throw new InheritanceNotApplicableException(
                    "This suggestion can't be added automatically. Make the change on the canvas or in the YAML editor."),
            };

            _parser.Parse(yaml); // never save a workflow the engine can't read
            await _workflows.UpdateAsync(workspaceId, workflowId, yaml, me.SlaThresholdMs, ct);
            _log.LogInformation("WorkflowInheritanceService.ApplyAsync exit workflow={WorkflowId} kind={Kind}", workflowId, kind);
            return yaml;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InheritanceNotApplicableException and not SfgParseException)
        {
            _log.LogError(ex, "WorkflowInheritanceService.ApplyAsync error workflow={WorkflowId} kind={Kind}", workflowId, kind);
            throw;
        }
    }

    private static string Steps(int n, string noun = "approval step") => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private async Task<WorkflowPerformance> PerformanceAsync(Guid workspaceId, Guid workflowId, CancellationToken ct) =>
        Performance(await _metrics.GetForDefinitionSinceAsync(workflowId, workspaceId, DateTime.UtcNow.AddDays(-30), ct));

    internal static WorkflowPerformance Performance(IReadOnlyCollection<WorkflowMetric> rows)
    {
        var finished = rows.Sum(m => m.RunsCompleted + m.RunsFailed);
        var completed = rows.Sum(m => m.RunsCompleted);
        var total = rows.Sum(m => m.RunsTotal);
        var breaches = rows.Sum(m => m.SlaBreachCount);
        var weighted = rows.Where(m => m.RunsTotal > 0).ToList();
        var avgMs = weighted.Count == 0 ? 0 : weighted.Sum(m => (double)m.AvgDurationMs * m.RunsTotal) / weighted.Sum(m => m.RunsTotal);
        return new WorkflowPerformance(
            finished,
            finished == 0 ? 0 : (int)Math.Round(completed * 100.0 / finished),
            total == 0 ? 100 : (int)Math.Round(100 - breaches * 100.0 / total),
            Math.Round(avgMs / 3_600_000, 1));
    }

    /// <summary>Why <paramref name="theirs"/> clearly beats <paramref name="mine"/>, or null when it doesn't.</summary>
    internal static string? Better(WorkflowPerformance theirs, WorkflowPerformance mine)
    {
        if (theirs.SuccessPercent >= mine.SuccessPercent + 10)
            return $"{theirs.SuccessPercent}% of its runs succeed, against {mine.SuccessPercent}% here.";
        if (theirs.SlaCompliancePercent >= mine.SlaCompliancePercent + 10)
            return $"It meets its SLA {theirs.SlaCompliancePercent}% of the time, against {mine.SlaCompliancePercent}% here.";
        if (mine.AvgHours > 0 && theirs.AvgHours > 0 && theirs.AvgHours <= mine.AvgHours * 0.7)
            return $"Its runs take {theirs.AvgHours:0.#} hours on average, against {mine.AvgHours:0.#} here.";
        return null;
    }

    /// <summary>The patterns compared between workflows.</summary>
    internal sealed record Patterns(
        (int Hours, string? OnTimeout)? GateTimeout,
        IReadOnlyList<string> GatesWithoutTimeout,
        RetryPolicy? ActionRetry,
        IReadOnlyList<string> ActionsWithoutRetry,
        bool NotifiesAfterApproval,
        bool UsesCompensation)
    {
        public static Patterns Of(WorkflowGraph g)
        {
            var gates = g.Nodes.Where(n => n.Type == NodeType.HumanGate).ToList();
            (int, string?)? timeout = null;
            var withoutTimeout = new List<string>();
            foreach (var gate in gates)
            {
                var hours = Int(gate, "timeout_hours") ?? (gate.TimeoutPolicy is { TimeoutSeconds: > 0 } tp ? tp.TimeoutSeconds / 3600 : null);
                var on = Str(gate, "on_timeout") ?? Str(gate, "onTimeout");
                if (hours is > 0) timeout ??= (hours.Value, on);
                else withoutTimeout.Add(gate.Id);
            }

            var actions = g.Nodes.Where(n => n.Type == NodeType.Action).ToList();
            var retry = actions.Select(a => a.RetryPolicy).FirstOrDefault(r => r is { MaxAttempts: > 1 });
            var withoutRetry = actions.Where(a => a.RetryPolicy is not { MaxAttempts: > 1 }).Select(a => a.Id).ToList();

            var gateIds = gates.Select(x => x.Id).ToHashSet();
            var notifies = g.Nodes.Where(n => n.Type == NodeType.Action &&
                    (Str(n, "connector_id") ?? Str(n, "connector")) is { } c && NotifyConnectors.Contains(c))
                .Any(n => Upstream(g, n.Id).Overlaps(gateIds));

            return new Patterns(timeout, withoutTimeout, retry, withoutRetry, notifies, g.Nodes.Any(n => n.Compensation is not null));
        }

        private static HashSet<string> Upstream(WorkflowGraph g, string nodeId)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>([nodeId]);
            while (stack.Count > 0)
                foreach (var e in g.IncomingEdges(stack.Pop()))
                    if (seen.Add(e.FromNodeId)) stack.Push(e.FromNodeId);
            return seen;
        }

        private static string? Str(SfgNode n, string key) =>
            n.Config.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && n.Config.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

        private static int? Int(SfgNode n, string key) =>
            n.Config.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && n.Config.RootElement.TryGetProperty(key, out var v)
                ? v.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number when v.TryGetInt32(out var i) => i,
                    System.Text.Json.JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
                    _ => null,
                }
                : null;
    }

    /// <summary>Edits node mappings in workflow YAML through YamlDotNet (never string manipulation).</summary>
    internal static class YamlNodes
    {
        public static string Edit(string yaml, IReadOnlyCollection<string> nodeIds, Action<YamlMappingNode> change)
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(yaml)) stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root ||
                !root.Children.TryGetValue(new YamlScalarNode("nodes"), out var nodesNode) || nodesNode is not YamlSequenceNode nodes)
                throw new InheritanceNotApplicableException("This workflow's YAML has no 'nodes' list to change. Fix it in the YAML editor first.");

            foreach (var item in nodes.Children.OfType<YamlMappingNode>())
                if (item.Children.TryGetValue(new YamlScalarNode("id"), out var id) && id is YamlScalarNode s && s.Value is { } v && nodeIds.Contains(v))
                    change(item);

            using var writer = new StringWriter();
            stream.Save(writer, assignAnchors: false);
            var text = writer.ToString().TrimEnd();
            return text.EndsWith("...", StringComparison.Ordinal) ? text[..^3].TrimEnd() + "\n" : text + "\n";
        }

        public static YamlMappingNode Child(YamlMappingNode node, string key)
        {
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var existing) && existing is YamlMappingNode map) return map;
            var created = new YamlMappingNode();
            node.Children[new YamlScalarNode(key)] = created;
            return created;
        }
    }
}

/// <summary>An inheritance suggestion that can't be applied as asked. Maps to HTTP 409.</summary>
public sealed class InheritanceNotApplicableException(string message) : AppException("INHERITANCE_NOT_APPLICABLE", message, 409);
