using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Workflow.Twin;

/// <summary>Why a simulated run went wrong at a node.</summary>
public enum TwinIssueKind
{
    /// <summary>No outgoing edge's condition held and there is no default branch: the path stops here.</summary>
    NoRoute,
    /// <summary>A router (meant to pick one branch) took two or more conditional branches at once.</summary>
    MultipleRoutes,
}

/// <summary>One outgoing edge of a node as evaluated in a simulated run.</summary>
/// <param name="ResolvedValues">Each placeholder in the condition and the text it resolved to (null when missing).</param>
public sealed record TwinEdgeEvaluation(
    string EdgeId, string ToNodeId, string? Label, string? Condition, bool IsDefault, bool Matched,
    IReadOnlyDictionary<string, string?> ResolvedValues);

public sealed record TwinIssue(TwinIssueKind Kind, string NodeId, IReadOnlyList<TwinEdgeEvaluation> Edges);

/// <summary>What one simulated run did.</summary>
public sealed record TwinTrace(
    bool ReachedEnd,
    IReadOnlyList<TwinIssue> Issues,
    IReadOnlySet<string> NodesVisited,
    IReadOnlySet<string> EdgesTaken);

/// <summary>
/// Workflow Twin (FUNCTIONAL.md §8.1): walks a workflow graph in memory for one input, with the engine's routing rules
/// (<see cref="ConditionEvaluator"/>, default branches only when no conditional sibling matched, label-only branches on
/// the upstream decision, each node entered once). Nothing is executed: actions, AI and other steps return a supplied
/// output (recorded from past runs, or empty), and human gates return a supplied decision. No database, no connectors,
/// no AI calls — safe to run thousands of times.
/// </summary>
public static class TwinSimulator
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    /// <param name="payload">The trigger payload (usually a JSON object).</param>
    /// <param name="nodeOutputs">Simulated outputs of action/AI/other steps by node id; missing ones output <c>{}</c>.</param>
    /// <param name="gateDecisions">Decision per gate id; gates not listed decide <see cref="Approved"/>.</param>
    public static TwinTrace Run(
        WorkflowGraph graph,
        JsonNode? payload,
        IReadOnlyDictionary<string, JsonNode?> nodeOutputs,
        IReadOnlyDictionary<string, string> gateDecisions)
    {
        var vars = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        SeedTrigger(vars, payload);

        var issues = new List<TwinIssue>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var reachedEnd = false;

        var trigger = graph.TriggerNode;
        visited.Add(trigger.Id);
        var work = new Queue<string>();
        Follow(trigger);

        while (work.Count > 0)
        {
            var node = graph.FindNode(work.Dequeue());
            if (node is null || !visited.Add(node.Id)) continue;

            switch (node.Type)
            {
                case NodeType.End:
                    reachedEnd = true;
                    break;
                case NodeType.Parallel:
                    foreach (var edge in graph.OutgoingEdges(node.Id))
                    {
                        taken.Add(edge.Id);
                        work.Enqueue(edge.ToNodeId);
                    }
                    break;
                case NodeType.Router or NodeType.IfElse or NodeType.Switch:
                    Follow(node);
                    break;
                case NodeType.HumanGate:
                    var decision = gateDecisions.TryGetValue(node.Id, out var d) ? d : Approved;
                    vars[$"{node.Id}.output"] = JsonSerializer.SerializeToElement(new { decision });
                    Follow(node);
                    break;
                default:
                    var output = nodeOutputs.TryGetValue(node.Id, out var o) ? o : null;
                    vars[$"{node.Id}.output"] = output is null
                        ? JsonSerializer.SerializeToElement(new { })
                        : JsonSerializer.SerializeToElement(output);
                    Follow(node);
                    break;
            }
        }

        return new TwinTrace(reachedEnd, issues, visited, taken);

        void Follow(SfgNode from)
        {
            var outgoing = graph.OutgoingEdges(from.Id).ToList();
            if (outgoing.Count == 0) return; // a node with no way out is the validator's concern, not routing's

            var evaluations = Evaluate(graph, from, outgoing, vars);
            var targets = evaluations.Where(e => e.Matched).ToList();
            if (targets.Count == 0)
            {
                issues.Add(new TwinIssue(TwinIssueKind.NoRoute, from.Id, evaluations));
                return;
            }

            var conditional = targets.Count(e => !e.IsDefault && (e.Condition is not null || e.Label is not null));
            if (from.Type is NodeType.Router or NodeType.IfElse or NodeType.Switch && conditional > 1)
                issues.Add(new TwinIssue(TwinIssueKind.MultipleRoutes, from.Id, evaluations));

            foreach (var target in targets)
            {
                taken.Add(target.EdgeId);
                work.Enqueue(target.ToNodeId);
            }
        }
    }

    /// <summary>Outgoing edges and whether each is followed — the same selection as the orchestrator's SelectTargets.</summary>
    private static List<TwinEdgeEvaluation> Evaluate(
        WorkflowGraph graph, SfgNode from, List<SfgEdge> outgoing, Dictionary<string, JsonElement> vars)
    {
        string? decision = null;
        if (outgoing.Any(IsLabelOnly)) decision = UpstreamDecision(graph, from.Id, vars);

        var results = new List<(SfgEdge Edge, bool Matched)>();
        var anyConditionalMatched = false;
        foreach (var edge in outgoing)
        {
            if (edge.IsDefault)
            {
                results.Add((edge, false)); // decided below
                continue;
            }
            bool matched;
            if (IsLabelOnly(edge) && decision is not null)
            {
                matched = DecisionMatches(edge.Via!, decision);
                if (matched) anyConditionalMatched = true;
            }
            else
            {
                matched = ConditionEvaluator.Evaluate(edge.Condition, vars);
                if (matched && !string.IsNullOrWhiteSpace(edge.Condition)) anyConditionalMatched = true;
            }
            results.Add((edge, matched));
        }

        return results.Select(r => new TwinEdgeEvaluation(
            r.Edge.Id,
            r.Edge.ToNodeId,
            r.Edge.Via,
            r.Edge.Condition,
            r.Edge.IsDefault,
            r.Edge.IsDefault ? !anyConditionalMatched : r.Matched,
            ResolveValues(r.Edge, vars, decision))).ToList();
    }

    private static Dictionary<string, string?> ResolveValues(SfgEdge edge, Dictionary<string, JsonElement> vars, string? decision)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(edge.Condition))
        {
            foreach (var path in ConditionEvaluator.PlaceholderPaths(edge.Condition))
                values[path] = ConditionEvaluator.Resolve(vars, path) is { } v && v.ValueKind is not JsonValueKind.Null
                    ? ConditionEvaluator.ToText(v)
                    : null;
        }
        else if (IsLabelOnly(edge))
        {
            values["decision"] = decision;
        }
        return values;
    }

    /// <summary>Seeds variables like the orchestrator: <c>trigger</c>, plus each top-level field of an object payload.</summary>
    private static void SeedTrigger(Dictionary<string, JsonElement> vars, JsonNode? payload)
    {
        if (payload is null) return;
        var element = JsonSerializer.SerializeToElement(payload);
        vars["trigger"] = element;
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Length is 0 or > 100 || property.Name.Contains('.')) continue;
            vars[property.Name] = property.Value.Clone();
        }
    }

    public static bool IsLabelOnly(SfgEdge edge) =>
        !edge.IsDefault && string.IsNullOrWhiteSpace(edge.Condition) && !string.IsNullOrWhiteSpace(edge.Via);

    /// <summary>The "decision" field of this node's output, else of its nearest predecessor that has one.</summary>
    private static string? UpstreamDecision(WorkflowGraph graph, string nodeId, Dictionary<string, JsonElement> vars)
    {
        foreach (var candidate in new[] { nodeId }.Concat(graph.IncomingEdges(nodeId).Select(e => e.FromNodeId)))
        {
            if (!vars.TryGetValue($"{candidate}.output", out var output)) continue;
            if (output.ValueKind == JsonValueKind.Object
                && output.TryGetProperty("decision", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();
        }
        return null;
    }

    /// <summary>Same normalisation as the orchestrator (approve/approved/yes… → approved).</summary>
    public static bool DecisionMatches(string via, string decision)
    {
        static string Norm(string v) => v.Trim().ToLowerInvariant() switch
        {
            "approve" or "approved" or "yes" or "accept" or "accepted" => Approved,
            "reject" or "rejected" or "no" or "decline" or "declined" or "denied" => Rejected,
            var other => other,
        };
        return Norm(via) == Norm(decision);
    }
}
