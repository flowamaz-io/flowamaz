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
    int Cases,
    bool FromRealPayload = false);

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
/// <item><b>Info</b>: a conditional branch no simulated input ever took; a gate whose "no" ends the run as Rejected
/// because the workflow has no rejected path (B10), so the author knows nothing runs after it.</item>
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
        var endsRejected = new Dictionary<string, int>(StringComparer.Ordinal);

        // Cases come in groups: the base input first, then one changed value at a time. An issue the base input already
        // has is reported once, from the base case, not again for every changed value that happens to share it.
        var baseIssues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var trace = TwinSimulator.Run(graph, c.Payload, c.NodeOutputs, c.GateDecisions);
            edgesTaken.UnionWith(trace.EdgesTaken);
            nodesVisited.UnionWith(trace.NodesVisited);
            if (trace.RejectedAt is { } gateId) endsRejected[gateId] = endsRejected.GetValueOrDefault(gateId) + 1;
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
        findings.AddRange(endsRejected.Select(kv => EndsRejected(graph, kv.Key, kv.Value)));
        findings.AddRange(ApprovedOnlyLinks(graph));

        // Approval steps are decision points too: every case is tried with them approved and rejected.
        var routers = graph.Nodes.Count(n => n.Type is NodeType.Router or NodeType.IfElse or NodeType.Switch or NodeType.HumanGate
            || graph.OutgoingEdges(n.Id).Any(e => e.Condition is not null || e.Via is not null || e.IsDefault));
        return new TwinReport(cases.Count, routers, findings
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.Cases)
            .ToList());
    }

    /// <summary>A gate's "no" ends the run as Rejected: by design for many workflows, but worth knowing (B10).</summary>
    private static TwinFinding EndsRejected(WorkflowGraph graph, string gateId, int cases)
    {
        var node = graph.FindNode(gateId);
        var label = node is null || string.IsNullOrWhiteSpace(node.Label) ? gateId : node.Label;
        return new TwinFinding(
            "ends-rejected", TwinSeverity.Info, gateId, label,
            $"Ends as Rejected when \"{label}\" is rejected",
            $"The workflow has no rejected path after \"{label}\", so a \"no\" ends the run with the status Rejected and the steps after it don't run. "
            + "Rejected runs are counted apart from failures.",
            "If something should still happen after a \"no\" (for example, telling the requester), add a branch labelled \"rejected\" after the approval step. "
            + "Its other, unlabelled link then runs only when the step is approved.",
            new Dictionary<string, string> { [gateId] = TwinSimulator.Rejected }, [], cases);
    }

    /// <summary>
    /// An unlabelled link out of an approval step that also has labelled branches runs only on "approved" (B10b). Said
    /// once per link, so authors who expected it to run on every decision see the rule.
    /// </summary>
    private static IEnumerable<TwinFinding> ApprovedOnlyLinks(WorkflowGraph graph)
    {
        foreach (var edge in graph.Edges.Where(e => TwinSimulator.IsApprovedOnlyLink(graph, e.FromNodeId, e)))
        {
            var gate = graph.FindNode(edge.FromNodeId)!;
            var gateLabel = string.IsNullOrWhiteSpace(gate.Label) ? gate.Id : gate.Label;
            var target = graph.FindNode(edge.ToNodeId) is { } t && !string.IsNullOrWhiteSpace(t.Label) ? t.Label : edge.ToNodeId;
            yield return new TwinFinding(
                "approved-only-link", TwinSeverity.Info, gate.Id, gateLabel,
                $"\"{target}\" runs only when \"{gateLabel}\" is approved",
                $"\"{gateLabel}\" has labelled branches, so its unlabelled link to \"{target}\" is treated as the approved path and does not run on a \"no\".",
                $"If \"{target}\" should run whatever the decision, run it before \"{gateLabel}\", or add it to each branch.",
                new Dictionary<string, string> { [gate.Id] = TwinSimulator.Approved }, [], 0); // a rule, not an input count
        }
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
                example, also, g.Count, FromRealPayload: g.Cause == "real");
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
            fix, example, also, g.Count, FromRealPayload: g.Cause == "real");
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
        if (e.Label is not null) return $"\"{name}\" on decision {e.Label}";
        // An approval step's unlabelled main path next to labelled branches (B10b).
        return e.ResolvedValues.ContainsKey("decision") ? $"\"{name}\" on decision approved" : $"\"{name}\" always";
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
