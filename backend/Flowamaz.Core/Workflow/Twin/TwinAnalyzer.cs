using System.Globalization;
using System.Text.Json.Nodes;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Workflow.Twin;

/// <summary>How serious a finding is. Breaks are shown as a red badge on the workflow.</summary>
public enum TwinSeverity { Break, Warning, Info }

/// <summary>One finding, as stored and shown. <see cref="Example"/> lists only the values that matter for it.</summary>
public sealed record TwinFinding(
    string Kind,
    TwinSeverity Severity,
    string NodeId,
    string NodeLabel,
    string Title,
    string Explanation,
    string Fix,
    IReadOnlyDictionary<string, string> Example,
    IReadOnlyList<string> AlsoFailsWith,
    int Cases);

/// <summary>The outcome of simulating one workflow.</summary>
public sealed record TwinReport(int CasesRun, int RoutersTested, IReadOnlyList<TwinFinding> Findings)
{
    public int Breaks => Findings.Count(f => f.Severity == TwinSeverity.Break);
    public int Warnings => Findings.Count(f => f.Severity == TwinSeverity.Warning);
}

/// <summary>
/// Runs every generated case through <see cref="TwinSimulator"/> and turns what goes wrong into a short list of
/// findings: one per node and cause, with the simplest input that shows it, how many cases hit it, and a fix.
/// <list type="bullet">
/// <item><b>Break</b>: a well-formed input (or a real past payload, or a gate decision) leaves the run with no route,
/// or makes a router take two branches; also a missing optional field that strands the run.</item>
/// <item><b>Warning</b>: only a missing required field or a value of the wrong type does it — nothing rejects such
/// input today, but the caller is expected not to send it.</item>
/// <item><b>Info</b>: a conditional branch no simulated input ever took.</item>
/// </list>
/// </summary>
public static class TwinAnalyzer
{
    public static TwinReport Analyse(WorkflowGraph graph, TwinHistory history, int maxCases = TwinCaseGenerator.DefaultMaxCases)
    {
        var cases = TwinCaseGenerator.Generate(graph, history, maxCases);
        var declared = graph.DeclaredInputs.ToDictionary(i => i.Name, StringComparer.Ordinal);
        var conditionPaths = TwinCaseGenerator.CandidateValues(graph).Keys.ToList();
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var edgesTaken = new HashSet<string>(StringComparer.Ordinal);
        var nodesVisited = new HashSet<string>(StringComparer.Ordinal);

        // Cases come in groups: the base input first, then one changed value at a time. An issue the base input already
        // has is reported once, from the base case, not again for every changed value that happens to share it.
        var baseIssues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var trace = TwinSimulator.Run(graph, c.Payload, c.NodeOutputs, c.GateDecisions);
            edgesTaken.UnionWith(trace.EdgesTaken);
            nodesVisited.UnionWith(trace.NodesVisited);
            if (c.VariedPath is null)
            {
                baseIssues.Clear();
                foreach (var i in trace.Issues) baseIssues.Add($"{i.Kind}|{i.NodeId}");
            }
            foreach (var issue in trace.Issues)
            {
                if (c.VariedPath is not null && baseIssues.Contains($"{issue.Kind}|{issue.NodeId}")) continue;
                var severity = SeverityOf(c, declared);
                var cause = c.VariedValue?.Kind switch
                {
                    TwinValueKind.Missing => "missing",
                    TwinValueKind.WrongType => "wrong-type",
                    // A real past payload that breaks it is the strongest evidence; keep it as its own finding.
                    TwinValueKind.Base when c.BaseLabel.StartsWith("a recent real payload", StringComparison.Ordinal) => "real",
                    _ => "valid",
                };
                var key = $"{issue.Kind}|{issue.NodeId}|{cause}";
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Group(issue, c, trace, severity, cause);
                    groups[key] = group;
                }
                group.Count++;
                group.Consider(c, trace, severity);
                if (DescribeTrigger(c) is { } changed) group.Values.Add(changed);
            }
        }

        var findings = groups.Values
            .Select(g => ToFinding(graph, g, declared, conditionPaths))
            .ToList();
        findings.AddRange(NeverTaken(graph, edgesTaken, nodesVisited, cases.Count));

        var routers = graph.Nodes.Count(n => n.Type is NodeType.Router or NodeType.IfElse or NodeType.Switch
            || graph.OutgoingEdges(n.Id).Any(e => e.Condition is not null || e.Via is not null || e.IsDefault));
        return new TwinReport(cases.Count, routers, findings
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.Cases)
            .ToList());
    }

    private static TwinSeverity SeverityOf(TwinCase c, Dictionary<string, WorkflowInput> declared) => c.VariedValue?.Kind switch
    {
        TwinValueKind.WrongType => TwinSeverity.Warning,
        TwinValueKind.Missing when c.VariedPath is { Source: TwinValueSource.Payload, Segments.Count: 1 } p
            && declared.TryGetValue(p.Segments[0], out var input) && input.Required => TwinSeverity.Warning,
        _ => TwinSeverity.Break,
    };

    private sealed class Group(TwinIssue issue, TwinCase example, TwinTrace trace, TwinSeverity severity, string cause)
    {
        public TwinIssue Issue { get; private set; } = issue;
        public TwinCase Example { get; private set; } = example;
        public TwinTrace Trace { get; private set; } = trace;
        public TwinSeverity Severity { get; private set; } = severity;
        public string Cause { get; } = cause;
        public int Count { get; set; }
        public SortedSet<string> Values { get; } = new(StringComparer.Ordinal);

        /// <summary>Keeps the most serious example, and among equals the one on the generated input (easiest to read).</summary>
        public void Consider(TwinCase c, TwinTrace t, TwinSeverity s)
        {
            var better = s < Severity
                || (s == Severity && Example.BaseLabel != "a generated input" && c.BaseLabel == "a generated input");
            if (!better) return;
            Severity = s;
            Example = c;
            Trace = t;
            Issue = t.Issues.First(i => i.Kind == Issue.Kind && i.NodeId == Issue.NodeId);
        }
    }

    private static TwinFinding ToFinding(
        WorkflowGraph graph, Group g, Dictionary<string, WorkflowInput> declared, List<TwinInputPath> conditionPaths)
    {
        var node = graph.FindNode(g.Issue.NodeId)!;
        var label = string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label;
        var example = ExampleValues(g.Example, g.Issue, conditionPaths);
        var trigger = DescribeTrigger(g.Example);
        var stops = g.Trace.ReachedEnd
            ? "The steps after it are skipped, but the run still finishes."
            : "The run never finishes: it stays Running with nothing left to do.";
        var branches = string.Join("; ", g.Issue.Edges.Select(DescribeEdge));
        var also = g.Values.Where(v => trigger is null || v != trigger).Take(5).ToList();

        if (g.Issue.Kind == TwinIssueKind.MultipleRoutes)
        {
            var matched = g.Issue.Edges.Where(e => e.Matched && !e.IsDefault).ToList();
            return new TwinFinding(
                "multiple-routes", g.Severity, node.Id, label,
                $"Takes {matched.Count} branches at once{(trigger is null ? "" : " when " + trigger)}",
                $"In \"{label}\", {string.Join(" and ", matched.Select(e => BranchName(graph, e)))} all match, so all of them run. Branches: {branches}.",
                "Make the conditions exclusive, for example use > on one branch and <= on the other, so exactly one branch matches every value.",
                example, also, g.Count);
        }

        var gateNote = GateNote(graph, g.Example, g.Issue);
        var (title, fix) = g.Cause switch
        {
            "missing" => (
                $"Gets stuck when {trigger ?? "a value"}",
                (IsRequired(g.Example, declared)
                    ? "The input is declared required, but Flowamaz doesn't reject runs without it. "
                    : string.Empty)
                + "Add a default (else) branch to catch it, or make sure every caller sends a value."),
            "wrong-type" => (
                $"Gets stuck when {trigger}",
                "A number written as text (with a comma, or not a number at all) makes every <, >, <= and >= comparison false. "
                + "Send plain numbers (5000, not \"5,000\"), or add a default (else) branch."),
            _ => (
                gateNote is not null ? $"Breaks when {gateNote}"
                    : trigger is not null ? $"Breaks when {trigger}" : $"Breaks for {g.Example.BaseLabel}",
                BoundaryFix(g.Example, g.Issue) ?? (gateNote is not null
                    ? "Add a branch for that decision (a \"rejected\" path), or a default (else) branch."
                    : "Add a branch that covers this value, or a default (else) branch.")),
        };

        return new TwinFinding(
            "no-route", g.Severity, node.Id, label, title,
            $"No branch of \"{label}\" matches{(trigger is null ? "" : " when " + trigger)}{(gateNote is null || trigger is null ? "" : " and " + gateNote)}. {stops} Branches: {branches}.",
            fix, example, also, g.Count);
    }

    /// <summary>"loan_amount = 50000" for the changed value; null for an unchanged base input.</summary>
    private static string? DescribeTrigger(TwinCase c) =>
        c.VariedPath is { } p && c.VariedValue is { } v && v.Kind != TwinValueKind.Base
            ? v.Remove ? $"{p.Display} is missing" : $"{p.Display} = {v.Describe()}"
            : null;

    private static string? GateNote(WorkflowGraph graph, TwinCase c, TwinIssue issue)
    {
        if (!issue.Edges.Any(e => e.ResolvedValues.ContainsKey("decision") || TwinSimulator.IsLabelOnly(
                graph.Edges.First(x => x.Id == e.EdgeId)))) return null;
        var rejected = c.GateDecisions.Where(kv => kv.Value == TwinSimulator.Rejected).Select(kv => kv.Key).ToList();
        if (rejected.Count == 0) return null;
        var names = rejected.Select(id => graph.FindNode(id) is { } n && !string.IsNullOrWhiteSpace(n.Label) ? $"\"{n.Label}\"" : id);
        return $"{string.Join(", ", names)} {(rejected.Count == 1 ? "is" : "are")} rejected";
    }

    private static bool IsRequired(TwinCase c, Dictionary<string, WorkflowInput> declared) =>
        c.VariedPath is { Source: TwinValueSource.Payload, Segments.Count: 1 } p
        && declared.TryGetValue(p.Segments[0], out var i) && i.Required;

    /// <summary>
    /// "One branch should include 50000" when the value sits exactly on a literal that every branch compares with a
    /// strict operator (the spec's "router uses &gt; not ≥").
    /// </summary>
    private static string? BoundaryFix(TwinCase c, TwinIssue issue)
    {
        if (c.VariedValue?.Value is not JsonValue jv || !decimal.TryParse(jv.ToJsonString().Trim('"'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        foreach (var edge in issue.Edges.Where(e => e.Condition is not null))
        {
            var parsed = ConditionEvaluator.Parse(edge.Condition!);
            if (parsed.Operator is not (">" or "<")) continue;
            var literal = ConditionEvaluator.SolePlaceholderPath(parsed.Left) is null ? parsed.Left : parsed.Right!;
            if (decimal.TryParse(ConditionEvaluator.Unquote(literal.Trim()), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                && n == value)
            {
                var number = n.ToString(CultureInfo.InvariantCulture);
                var strict = issue.Edges.Where(e => e.Condition is not null)
                    .Select(e => (Edge: e, Parsed: ConditionEvaluator.Parse(e.Condition!)))
                    .Where(x => x.Parsed.Operator is ">" or "<")
                    .Select(x => $"\"{x.Edge.Label ?? x.Edge.ToNodeId}\" uses {x.Parsed.Operator}")
                    .ToList();
                return $"Nothing handles exactly {number}: {string.Join(" and ", strict)}. Change one of them to <= or >= "
                       + $"(whichever side {number} belongs on), or add a default (else) branch.";
            }
        }
        return null;
    }

    /// <summary>The values of the paths this node's conditions read, plus the changed value and gate decisions.</summary>
    private static Dictionary<string, string> ExampleValues(TwinCase c, TwinIssue issue, List<TwinInputPath> conditionPaths)
    {
        var example = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in issue.Edges)
            foreach (var (path, value) in edge.ResolvedValues)
                if (path != "decision") example[path] = value ?? "(missing)";
        if (c.VariedPath is { } p && c.VariedValue is { } v && v.Kind != TwinValueKind.Base)
            example[p.Display] = v.Describe();
        foreach (var (gate, decision) in c.GateDecisions)
            if (issue.Edges.Any(e => e.ResolvedValues.ContainsKey("decision")) || decision == TwinSimulator.Rejected)
                example[$"gate {gate}"] = decision;
        _ = conditionPaths;
        return example;
    }

    private static string DescribeEdge(TwinEdgeEvaluation e)
    {
        var name = e.Label ?? e.ToNodeId;
        if (e.IsDefault) return $"\"{name}\" (default)";
        if (e.Condition is not null) return $"\"{name}\" when {e.Condition.Trim()}";
        return e.Label is not null ? $"\"{name}\" on decision {e.Label}" : $"\"{name}\" always";
    }

    private static string BranchName(WorkflowGraph graph, TwinEdgeEvaluation e) =>
        $"\"{e.Label ?? (graph.FindNode(e.ToNodeId) is { } n && !string.IsNullOrWhiteSpace(n.Label) ? n.Label : e.ToNodeId)}\""
        + (e.Condition is null ? string.Empty : $" ({e.Condition.Trim()})");

    /// <summary>Conditional branches of visited nodes that no case ever took.</summary>
    private static IEnumerable<TwinFinding> NeverTaken(WorkflowGraph graph, HashSet<string> taken, HashSet<string> visited, int cases)
    {
        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Condition) || taken.Contains(edge.Id) || !visited.Contains(edge.FromNodeId)) continue;
            var from = graph.FindNode(edge.FromNodeId)!;
            var label = string.IsNullOrWhiteSpace(from.Label) ? from.Id : from.Label;
            var name = edge.Via ?? (graph.FindNode(edge.ToNodeId)?.Label is { Length: > 0 } l ? l : edge.ToNodeId);
            yield return new TwinFinding(
                "never-taken", TwinSeverity.Info, from.Id, label,
                $"Branch \"{name}\" was never taken",
                $"None of {cases.ToString("N0", CultureInfo.InvariantCulture)} simulated runs took \"{name}\" from \"{label}\" (condition: {edge.Condition.Trim()}). "
                + "It may never be true — for example it reads a value no step provides, or an earlier branch always wins.",
                "Check the condition's field name and value against what the trigger or the earlier step really sends.",
                new Dictionary<string, string>(), [], 0);
        }
    }
}
