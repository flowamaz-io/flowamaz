using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Workflow.Twin;

/// <summary>Where a value a condition reads comes from.</summary>
public enum TwinValueSource { Payload, NodeOutput }

/// <summary>
/// A value a condition reads (<c>{{ loan_amount }}</c>, <c>{{ trigger.order.total }}</c>,
/// <c>{{ check-budget.output.remaining }}</c>), resolved to where the Twin can vary it.
/// </summary>
/// <param name="Display">The path as written in the workflow, for messages.</param>
/// <param name="NodeId">For node outputs: the node whose output holds it.</param>
/// <param name="Segments">Path inside the payload (or inside the node's output).</param>
public sealed record TwinInputPath(string Display, TwinValueSource Source, string? NodeId, IReadOnlyList<string> Segments)
{
    public string Key => Source == TwinValueSource.Payload ? "payload:" + string.Join('.', Segments) : $"{NodeId}:" + string.Join('.', Segments);
}

/// <summary>How a case's value was chosen. Decides how serious a break it finds is.</summary>
public enum TwinValueKind
{
    /// <summary>The base input unchanged (a past payload or the synthesised one).</summary>
    Base,
    /// <summary>A well-formed value at or next to a literal a condition compares with.</summary>
    Boundary,
    /// <summary>Another well-formed value (a string that matches no literal, a different letter case…).</summary>
    Valid,
    /// <summary>The field is left out, null or empty.</summary>
    Missing,
    /// <summary>A value of the wrong type for the comparison ("abc", "5,000" where a number is compared).</summary>
    WrongType,
}

/// <summary>A value to try for one path.</summary>
public sealed record TwinValue(JsonNode? Value, TwinValueKind Kind, bool Remove = false)
{
    public string Describe() => Remove ? "missing" : Value is null ? "null" : Value.ToJsonString();
}

/// <summary>One simulated run's input.</summary>
public sealed record TwinCase(
    JsonNode? Payload,
    IReadOnlyDictionary<string, JsonNode?> NodeOutputs,
    IReadOnlyDictionary<string, string> GateDecisions,
    TwinInputPath? VariedPath,
    TwinValue? VariedValue,
    string BaseLabel);

/// <summary>What the Twin knows about past runs.</summary>
/// <param name="PastPayloads">Recent real trigger payloads, newest first.</param>
/// <param name="RecordedOutputs">The latest recorded output per node id from real runs.</param>
public sealed record TwinHistory(IReadOnlyList<JsonNode?> PastPayloads, IReadOnlyDictionary<string, JsonNode?> RecordedOutputs)
{
    public static readonly TwinHistory Empty = new([], new Dictionary<string, JsonNode?>());
}

/// <summary>
/// Generates the inputs the Workflow Twin runs (FUNCTIONAL.md §8.1). From every condition in the workflow it finds the
/// values that are read and the literals they are compared with, then tries boundary values (the literal and its
/// neighbours), other well-formed values, missing/null/empty values and wrong types. Each is tried on top of each base
/// input (the synthesised one and recent real payloads) and with each set of gate decisions (all approved, all rejected,
/// and each gate rejected on its own). One value is changed per case, so a finding names exactly what breaks.
/// </summary>
public static class TwinCaseGenerator
{
    public const int DefaultMaxCases = 5000;
    private const int MaxPastPayloads = 10;
    private const int MaxSingleGateRejections = 8;

    /// <summary>The values each condition-read path will be tried with.</summary>
    public static IReadOnlyDictionary<TwinInputPath, IReadOnlyList<TwinValue>> CandidateValues(WorkflowGraph graph)
    {
        var literals = new Dictionary<string, (TwinInputPath Path, List<string> Literals, bool Numeric, bool Truthiness)>();
        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Condition)) continue;
            var parsed = ConditionEvaluator.Parse(edge.Condition);
            if (parsed.Operator is null)
            {
                if (ConditionEvaluator.SolePlaceholderPath(parsed.Left) is { } p && ToPath(graph, p) is { } tp)
                    Note(tp, null, false, truthiness: true);
                continue;
            }

            var leftPath = ConditionEvaluator.SolePlaceholderPath(parsed.Left);
            var rightPath = ConditionEvaluator.SolePlaceholderPath(parsed.Right!);
            var numericOp = parsed.Operator is ">" or ">=" or "<" or "<=";
            if (leftPath is not null && ToPath(graph, leftPath) is { } lp)
                Note(lp, rightPath is null ? ConditionEvaluator.Unquote(parsed.Right!.Trim()) : null, numericOp, false);
            if (rightPath is not null && ToPath(graph, rightPath) is { } rp)
                Note(rp, leftPath is null ? ConditionEvaluator.Unquote(parsed.Left.Trim()) : null, numericOp, false);
        }

        var declared = graph.DeclaredInputs.ToDictionary(i => i.Name, StringComparer.Ordinal);
        var result = new Dictionary<TwinInputPath, IReadOnlyList<TwinValue>>();
        foreach (var (_, entry) in literals)
        {
            var declaredType = entry.Path.Source == TwinValueSource.Payload && entry.Path.Segments.Count == 1
                && declared.TryGetValue(entry.Path.Segments[0], out var input) ? input.Type : null;
            var numeric = entry.Numeric || declaredType is "number" or "integer"
                || (entry.Literals.Count > 0 && entry.Literals.All(IsNumber));
            result[entry.Path] = Values(entry.Literals, numeric, entry.Truthiness);
        }
        return result;

        void Note(TwinInputPath path, string? literal, bool numericOp, bool truthiness)
        {
            if (!literals.TryGetValue(path.Key, out var entry))
            {
                entry = (path, [], false, false);
            }
            if (literal is not null && !entry.Literals.Contains(literal)) entry.Literals.Add(literal);
            literals[path.Key] = (entry.Path, entry.Literals, entry.Numeric || numericOp, entry.Truthiness || truthiness);
        }
    }

    /// <summary>All cases, capped at <paramref name="maxCases"/> (the first base and gate set are kept whole).</summary>
    public static IReadOnlyList<TwinCase> Generate(WorkflowGraph graph, TwinHistory history, int maxCases = DefaultMaxCases)
    {
        var candidates = CandidateValues(graph);
        var bases = Bases(graph, history, candidates);
        var gateSets = GateSets(graph);
        var baseOutputs = BaseOutputs(graph, history.RecordedOutputs, candidates);

        var cases = new List<TwinCase>();
        foreach (var gates in gateSets)
        {
            foreach (var (label, payload) in bases)
            {
                if (!Add(new TwinCase(payload, baseOutputs, gates, null, new TwinValue(null, TwinValueKind.Base), label))) return cases;
                foreach (var (path, values) in candidates)
                {
                    foreach (var value in values)
                    {
                        var (p, o) = Apply(payload, baseOutputs, path, value);
                        if (!Add(new TwinCase(p, o, gates, path, value, label))) return cases;
                    }
                }
            }
        }
        return cases;

        bool Add(TwinCase c)
        {
            if (cases.Count >= maxCases) return false;
            cases.Add(c);
            return true;
        }
    }

    /// <summary>Resolves a placeholder path to where its value comes from; null for values the Twin can't set.</summary>
    public static TwinInputPath? ToPath(WorkflowGraph graph, string raw)
    {
        var path = raw.StartsWith("variables.", StringComparison.Ordinal) ? raw["variables.".Length..] : raw;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        if (segments[0] == "trigger")
            return segments.Length > 1 ? new TwinInputPath(raw, TwinValueSource.Payload, null, segments[1..]) : null;

        // {{ node-id.output.field }} — longest node id that matches (ids may contain dots? they don't, but be safe).
        for (var take = segments.Length - 1; take >= 1; take--)
        {
            var nodeId = string.Join('.', segments, 0, take);
            if (segments[take] != "output" || graph.FindNode(nodeId) is not { } node) continue;
            if (node.Type is NodeType.HumanGate) return null; // gate outputs are covered by the gate decisions
            return new TwinInputPath(raw, TwinValueSource.NodeOutput, nodeId, segments[(take + 1)..]);
        }

        return new TwinInputPath(raw, TwinValueSource.Payload, null, segments);
    }

    private static IReadOnlyList<TwinValue> Values(List<string> literals, bool numeric, bool truthiness)
    {
        var values = new List<TwinValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(TwinValue v)
        {
            if (seen.Add(v.Kind + "|" + v.Describe())) values.Add(v);
        }

        if (numeric)
        {
            foreach (var literal in literals.Where(IsNumber))
            {
                var n = decimal.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
                var step = n == decimal.Truncate(n) ? 1m : 0.01m;
                Add(new TwinValue(JsonValue.Create(n), TwinValueKind.Boundary));
                Add(new TwinValue(JsonValue.Create(n - step), TwinValueKind.Boundary));
                Add(new TwinValue(JsonValue.Create(n + step), TwinValueKind.Boundary));
                if (step == 1m)
                {
                    Add(new TwinValue(JsonValue.Create(n - 0.01m), TwinValueKind.Boundary));
                    Add(new TwinValue(JsonValue.Create(n + 0.01m), TwinValueKind.Boundary));
                }
                // The same number as text with a thousands separator, as forms and spreadsheets send it.
                if (Math.Abs(n) >= 1000)
                    Add(new TwinValue(JsonValue.Create(n.ToString("#,0.##", CultureInfo.InvariantCulture)), TwinValueKind.WrongType));
            }
            Add(new TwinValue(JsonValue.Create(0), TwinValueKind.Valid));
            Add(new TwinValue(JsonValue.Create(-1), TwinValueKind.Valid));
            Add(new TwinValue(JsonValue.Create("abc"), TwinValueKind.WrongType));
        }
        else if (truthiness)
        {
            Add(new TwinValue(JsonValue.Create(true), TwinValueKind.Valid));
            Add(new TwinValue(JsonValue.Create(false), TwinValueKind.Valid));
            Add(new TwinValue(JsonValue.Create("false"), TwinValueKind.Valid));
            Add(new TwinValue(JsonValue.Create(0), TwinValueKind.Valid));
        }
        else
        {
            foreach (var literal in literals)
            {
                Add(new TwinValue(JsonValue.Create(literal), TwinValueKind.Boundary));
                var otherCase = literal == literal.ToLowerInvariant() ? Capitalise(literal) : literal.ToLowerInvariant();
                if (otherCase != literal) Add(new TwinValue(JsonValue.Create(otherCase), TwinValueKind.Valid));
                Add(new TwinValue(JsonValue.Create(literal + " "), TwinValueKind.Valid));
            }
            Add(new TwinValue(JsonValue.Create("something-else"), TwinValueKind.Valid));
        }

        Add(new TwinValue(null, TwinValueKind.Missing, Remove: true));
        Add(new TwinValue(null, TwinValueKind.Missing));
        Add(new TwinValue(JsonValue.Create(string.Empty), TwinValueKind.Missing));
        return values;
    }

    /// <summary>The synthesised input (reaches the routers with sensible values), then recent real payloads.</summary>
    private static List<(string Label, JsonNode? Payload)> Bases(
        WorkflowGraph graph, TwinHistory history, IReadOnlyDictionary<TwinInputPath, IReadOnlyList<TwinValue>> candidates)
    {
        var synthetic = new JsonObject();
        foreach (var input in graph.DeclaredInputs)
        {
            JsonNode? value = input.Type switch
            {
                "number" or "integer" => JsonValue.Create(100),
                "boolean" => JsonValue.Create(true),
                "string" or null => JsonValue.Create("sample"),
                _ => null,
            };
            if (value is not null) synthetic[input.Name] = value;
        }
        // A well-formed value for every path a condition reads, so later routers are reached too. Prefer one that
        // routes cleanly (no stuck path, no double branch, reaches an end) so the base input itself is a good run and
        // each changed value shows exactly what it breaks.
        var outputs = BaseOutputs(graph, history.RecordedOutputs, candidates);
        var approveAll = GateSets(graph)[0];
        foreach (var (path, values) in candidates)
        {
            if (path.Source != TwinValueSource.Payload) continue;
            var wellFormed = values.Where(v => v.Kind is TwinValueKind.Boundary or TwinValueKind.Valid).ToList();
            if (wellFormed.Count == 0) continue;
            var chosen = wellFormed[0];
            foreach (var candidate in wellFormed)
            {
                var trial = (JsonObject)synthetic.DeepClone();
                SetPath(trial, path.Segments, candidate.Value?.DeepClone());
                var trace = TwinSimulator.Run(graph, trial, outputs, approveAll);
                if (trace.Issues.Count == 0 && trace.ReachedEnd)
                {
                    chosen = candidate;
                    break;
                }
            }
            SetPath(synthetic, path.Segments, chosen.Value?.DeepClone());
        }

        var bases = new List<(string, JsonNode?)> { ("a generated input", synthetic) };
        var n = 0;
        foreach (var past in history.PastPayloads)
        {
            if (past is not JsonObject || n >= MaxPastPayloads) continue;
            bases.Add(($"a recent real payload (#{++n})", past.DeepClone()));
        }
        return bases;
    }

    private static Dictionary<string, JsonNode?> BaseOutputs(
        WorkflowGraph graph, IReadOnlyDictionary<string, JsonNode?> recorded, IReadOnlyDictionary<TwinInputPath, IReadOnlyList<TwinValue>> candidates)
    {
        _ = graph;
        var outputs = recorded.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone(), StringComparer.Ordinal);
        foreach (var (path, values) in candidates)
        {
            if (path.Source != TwinValueSource.NodeOutput || path.NodeId is null) continue;
            if (outputs.TryGetValue(path.NodeId, out var existing) && existing is JsonObject recordedOutput
                && GetPath(recordedOutput, path.Segments) is not null)
                continue; // a recorded real value is the better base
            var first = values.FirstOrDefault(v => v.Kind is TwinValueKind.Boundary or TwinValueKind.Valid);
            if (first is null) continue;
            var output = outputs.TryGetValue(path.NodeId, out var o) && o is JsonObject obj ? obj : new JsonObject();
            SetPath(output, path.Segments, first.Value?.DeepClone());
            outputs[path.NodeId] = output;
        }
        return outputs;
    }

    private static (JsonNode? Payload, Dictionary<string, JsonNode?> Outputs) Apply(
        JsonNode? basePayload, Dictionary<string, JsonNode?> baseOutputs, TwinInputPath path, TwinValue value)
    {
        if (path.Source == TwinValueSource.Payload)
        {
            var payload = basePayload?.DeepClone() as JsonObject ?? new JsonObject();
            if (value.Remove) RemovePath(payload, path.Segments);
            else SetPath(payload, path.Segments, value.Value?.DeepClone());
            return (payload, baseOutputs);
        }

        var outputs = new Dictionary<string, JsonNode?>(baseOutputs, StringComparer.Ordinal);
        var output = baseOutputs.TryGetValue(path.NodeId!, out var o) && o is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject();
        if (path.Segments.Count == 0)
        {
            outputs[path.NodeId!] = value.Remove ? null : value.Value?.DeepClone();
            return (basePayload, outputs);
        }
        if (value.Remove) RemovePath(output, path.Segments);
        else SetPath(output, path.Segments, value.Value?.DeepClone());
        outputs[path.NodeId!] = output;
        return (basePayload, outputs);
    }

    /// <summary>All gates approved, all rejected, then each gate rejected on its own (up to 8).</summary>
    private static List<IReadOnlyDictionary<string, string>> GateSets(WorkflowGraph graph)
    {
        var gates = graph.Nodes.Where(n => n.Type == NodeType.HumanGate).Select(n => n.Id).ToList();
        var sets = new List<IReadOnlyDictionary<string, string>> { gates.ToDictionary(g => g, _ => TwinSimulator.Approved) };
        if (gates.Count == 0) return sets;
        sets.Add(gates.ToDictionary(g => g, _ => TwinSimulator.Rejected));
        if (gates.Count > 1)
        {
            foreach (var rejected in gates.Take(MaxSingleGateRejections))
                sets.Add(gates.ToDictionary(g => g, g => g == rejected ? TwinSimulator.Rejected : TwinSimulator.Approved));
        }
        return sets;
    }

    public static JsonNode? GetPath(JsonNode? root, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segment, out var child) => child,
                JsonArray arr when int.TryParse(segment, out var i) && i >= 0 && i < arr.Count => arr[i],
                _ => null,
            };
            if (current is null) return null;
        }
        return current;
    }

    private static void SetPath(JsonObject root, IReadOnlyList<string> segments, JsonNode? value)
    {
        if (segments.Count == 0) return;
        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current[segments[i]] is not JsonObject next)
            {
                next = new JsonObject();
                current[segments[i]] = next;
            }
            current = next;
        }
        current[segments[^1]] = value;
    }

    private static void RemovePath(JsonObject root, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) return;
        if (GetPath(root, segments.Take(segments.Count - 1).ToList()) is JsonObject parent)
            parent.Remove(segments[^1]);
    }

    private static bool IsNumber(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
