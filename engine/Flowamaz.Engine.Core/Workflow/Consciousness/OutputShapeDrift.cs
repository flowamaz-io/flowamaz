using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flowamaz.Core.Workflow.Consciousness;

/// <summary>How a field of a step's output changed between earlier runs and recent ones.</summary>
public enum DriftKind { Removed, TypeChanged }

/// <param name="Path">Field path inside the step's output (<c>customer.id</c>; array items are <c>items[]</c>).</param>
/// <param name="BaselineType">JSON type the field had in earlier runs (string, number, boolean, object, array).</param>
/// <param name="RecentType">Its type in recent runs; null when it is missing.</param>
/// <param name="BaselineShare">Share of earlier outputs that had the field with <paramref name="BaselineType"/> (0–1).</param>
/// <param name="RecentShare">Share of recent outputs that still have it with that type (0–1).</param>
/// <param name="UsedBy">Places in the workflow that read this field (<c>{{ step.output.path }}</c>), as written.</param>
public sealed record FieldDrift(
    string Path, DriftKind Kind, string BaselineType, string? RecentType, double BaselineShare, double RecentShare,
    IReadOnlyList<string> UsedBy);

/// <summary>
/// Workflow Consciousness (FUNCTIONAL.md §8.11) — "connector behaviour changes detected before failure". Compares the
/// shape of a step's output in earlier runs with recent runs: a field that was (nearly) always there and is now mostly
/// missing, or that changed type (a number now sent as text), is reported, and flagged as risky when the workflow reads
/// it. Values are never kept, only paths and types.
/// </summary>
public static partial class OutputShapeDrift
{
    /// <summary>A field counts as part of the earlier shape when at least this share of earlier outputs had it.</summary>
    public const double StableShare = 0.9;
    /// <summary>It has drifted when fewer than this share of recent outputs still have it (with the same type).</summary>
    public const double DriftShare = 0.5;
    public const int MinBaseline = 5;
    public const int MinRecent = 3;
    private const int MaxDepth = 5;
    private const int MaxPathsPerOutput = 300;

    /// <summary>
    /// Fields of one step's output that drifted. Empty when there are too few runs on either side to judge
    /// (<see cref="MinBaseline"/> earlier, <see cref="MinRecent"/> recent).
    /// </summary>
    /// <param name="references">What the workflow reads from this step: output paths as returned by <see cref="ReferencedPaths"/>.</param>
    public static IReadOnlyList<FieldDrift> Compare(
        IReadOnlyList<JsonElement> baseline, IReadOnlyList<JsonElement> recent, IReadOnlyDictionary<string, List<string>> references)
    {
        if (baseline.Count < MinBaseline || recent.Count < MinRecent) return [];

        var baselineShapes = baseline.Select(Shape).ToList();
        var recentShapes = recent.Select(Shape).ToList();

        var drifts = new List<FieldDrift>();
        var candidates = baselineShapes.SelectMany(s => s.Keys).Distinct(StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            // The type the field (almost) always had before.
            var types = baselineShapes.Where(s => s.ContainsKey(path)).GroupBy(s => s[path]).OrderByDescending(g => g.Count()).First();
            var baselineShare = (double)types.Count() / baselineShapes.Count;
            if (baselineShare < StableShare) continue;
            var baselineType = types.Key;

            var sameType = recentShapes.Count(s => s.TryGetValue(path, out var t) && t == baselineType);
            var recentShare = (double)sameType / recentShapes.Count;
            if (recentShare >= DriftShare) continue;

            // Report the field closest to the root: if "customer" vanished, don't also list "customer.id".
            if (drifts.Any(d => path.StartsWith(d.Path + ".", StringComparison.Ordinal) || path.StartsWith(d.Path + "[]", StringComparison.Ordinal)))
                continue;

            var present = recentShapes.Where(s => s.ContainsKey(path)).ToList();
            var recentType = present.Count == 0 ? null
                : present.GroupBy(s => s[path]).OrderByDescending(g => g.Count()).First().Key;
            var kind = present.Count * 2 >= recentShapes.Count && recentType != baselineType ? DriftKind.TypeChanged : DriftKind.Removed;

            drifts.Add(new FieldDrift(path, kind, baselineType, kind == DriftKind.Removed ? null : recentType,
                Math.Round(baselineShare, 2), Math.Round(recentShare, 2), UsedBy(path, references)));
        }
        return drifts.OrderBy(d => d.UsedBy.Count == 0).ThenBy(d => d.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The output paths of <paramref name="nodeId"/> that the workflow text reads, from <c>{{ nodeId.output.path }}</c>
    /// and <c>${nodeId.output.path}</c>, with array indexes written as <c>[]</c>. Value: the placeholders as written.
    /// </summary>
    public static Dictionary<string, List<string>> ReferencedPaths(string workflowText, string nodeId)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in ConditionEvaluator.PlaceholderPaths(workflowText ?? string.Empty))
        {
            var p = path.StartsWith("variables.", StringComparison.Ordinal) ? path["variables.".Length..] : path;
            var prefix = nodeId + ".output.";
            if (!p.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var inner = IndexSegment().Replace(p[prefix.Length..], "[]").Replace(".[]", "[]");
            if (!result.TryGetValue(inner, out var list)) result[inner] = list = [];
            if (!list.Contains("{{ " + path + " }}")) list.Add("{{ " + path + " }}");
        }
        return result;
    }

    /// <summary>Path → JSON type of every field in an output (nulls count as missing).</summary>
    public static Dictionary<string, string> Shape(JsonElement output)
    {
        var shape = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(output, string.Empty, 0, shape);
        return shape;
    }

    private static void Walk(JsonElement element, string path, int depth, Dictionary<string, string> shape)
    {
        if (shape.Count >= MaxPathsPerOutput || depth > MaxDepth) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (path.Length > 0) shape[path] = "object";
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", depth + 1, shape);
                break;
            case JsonValueKind.Array:
                if (path.Length > 0) shape[path] = "array";
                foreach (var item in element.EnumerateArray().Take(3))
                    Walk(item, path + "[]", depth + 1, shape);
                break;
            case JsonValueKind.String: if (path.Length > 0) shape[path] = "string"; break;
            case JsonValueKind.Number: if (path.Length > 0) shape[path] = "number"; break;
            case JsonValueKind.True or JsonValueKind.False: if (path.Length > 0) shape[path] = "boolean"; break;
            // Null and undefined: treated as missing.
        }
    }

    private static IReadOnlyList<string> UsedBy(string path, IReadOnlyDictionary<string, List<string>> references) =>
        references
            .Where(r => r.Key == path || r.Key.StartsWith(path + ".", StringComparison.Ordinal) || r.Key.StartsWith(path + "[]", StringComparison.Ordinal))
            .SelectMany(r => r.Value)
            .Distinct()
            .ToList();

    [GeneratedRegex(@"\.(\d+)(?=\.|$)")]
    private static partial Regex IndexSegment();
}
