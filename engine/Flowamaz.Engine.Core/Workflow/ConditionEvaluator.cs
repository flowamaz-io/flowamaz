using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flowamaz.Core.Workflow;

/// <summary>A condition split at its comparison operator. <see cref="Operator"/> is null for a truthiness test.</summary>
public sealed record ParsedCondition(string Left, string? Operator, string? Right);

/// <summary>
/// The rules the engine uses for <c>{{ name }}</c> placeholders and edge conditions (==, !=, &gt;, &gt;=, &lt;, &lt;=, or
/// truthiness), as pure functions over a variable map. The live engine (VariableEvaluationService) and the Workflow Twin
/// simulator both evaluate through this class, so a simulated run routes exactly like a real one.
/// </summary>
public static partial class ConditionEvaluator
{
    /// <summary>Comparison operators, multi-character first so "&gt;=" is not read as "&gt;".</summary>
    public static readonly string[] Operators = ["==", "!=", ">=", "<=", ">", "<"];

    /// <summary>The canvas condition builder writes <c>${name}</c>; conditions accept it alongside <c>{{ name }}</c>.</summary>
    public static string NormalizePlaceholders(string condition) =>
        DollarPlaceholderRegex().Replace(condition, m => "{{ " + m.Groups[1].Value + " }}");

    /// <summary>Splits a (normalised) condition at its first operator outside quotes and placeholders.</summary>
    public static ParsedCondition Parse(string condition)
    {
        var normalized = NormalizePlaceholders(condition);
        foreach (var op in Operators)
        {
            var idx = IndexOfOperator(normalized, op);
            if (idx < 0) continue;
            return new ParsedCondition(normalized[..idx].Trim(), op, normalized[(idx + op.Length)..].Trim());
        }
        return new ParsedCondition(normalized.Trim(), null, null);
    }

    /// <summary>Evaluates an edge condition against variables. Empty conditions hold.</summary>
    public static bool Evaluate(string? condition, IReadOnlyDictionary<string, JsonElement> vars)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var parsed = Parse(condition);
        if (parsed.Operator is { } op)
        {
            var left = Unquote(Interpolate(parsed.Left, vars).Trim());
            var right = Unquote(Interpolate(parsed.Right!, vars).Trim());
            return Compare(left, right, op);
        }

        // No operator → truthiness of the interpolated value.
        var value = Interpolate(parsed.Left, vars).Trim();
        return value.Length > 0
               && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
               && value != "0";
    }

    /// <summary>Replaces every <c>{{ path }}</c> with its value's text; unknown paths become empty.</summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, JsonElement> vars)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return PlaceholderRegex().Replace(template, m =>
            Resolve(vars, m.Groups[1].Value) is { } value ? ToText(value) : string.Empty);
    }

    /// <summary>The placeholder paths a text refers to (<c>{{ amount }}</c> → <c>amount</c>).</summary>
    public static IReadOnlyList<string> PlaceholderPaths(string text) =>
        PlaceholderRegex().Matches(NormalizePlaceholders(text)).Select(m => m.Groups[1].Value).ToList();

    /// <summary>When the whole text is one placeholder, its path; otherwise null.</summary>
    public static string? SolePlaceholderPath(string text)
    {
        var match = PlaceholderRegex().Match(text.Trim());
        return match.Success && match.Index == 0 && match.Length == text.Trim().Length ? match.Groups[1].Value : null;
    }

    public static bool Compare(string left, string right, string op)
    {
        var numeric = decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                      & decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var r);
        return op switch
        {
            "==" => numeric ? l == r : string.Equals(left, right, StringComparison.Ordinal),
            "!=" => numeric ? l != r : !string.Equals(left, right, StringComparison.Ordinal),
            ">" => numeric && l > r,
            ">=" => numeric && l >= r,
            "<" => numeric && l < r,
            "<=" => numeric && l <= r,
            _ => false,
        };
    }

    /// <summary>
    /// Resolves a placeholder path. Accepts the spec form <c>variables.amount</c>, plain <c>amount</c>,
    /// and paths into JSON values such as <c>create-issue.output.number</c> or <c>rows.0.id</c>
    /// (longest matching variable name wins, the rest navigates object keys / array indexes).
    /// </summary>
    public static JsonElement? Resolve(IReadOnlyDictionary<string, JsonElement> vars, string path)
    {
        if (path.StartsWith("variables.", StringComparison.Ordinal) && !vars.ContainsKey(path))
            path = path["variables.".Length..];
        if (vars.TryGetValue(path, out var direct)) return direct;

        var segments = path.Split('.');
        for (var take = segments.Length - 1; take >= 1; take--)
        {
            var key = string.Join('.', segments, 0, take);
            if (!vars.TryGetValue(key, out var current)) continue;

            for (var i = take; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var child))
                    current = child;
                else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index)
                         && index >= 0 && index < current.GetArrayLength())
                    current = current[index];
                else
                    return null;
            }
            return current;
        }
        return null;
    }

    /// <summary>A JSON string unwraps to its text; anything else uses raw JSON.</summary>
    public static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    public static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    // Finds an operator outside quotes and outside {{ }} placeholders.
    private static int IndexOfOperator(string text, string op)
    {
        var inQuote = '\0';
        var depth = 0;
        for (var i = 0; i <= text.Length - op.Length; i++)
        {
            var c = text[i];
            if (inQuote != '\0') { if (c == inQuote) inQuote = '\0'; continue; }
            if (c is '"' or '\'') { inQuote = c; continue; }
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { depth++; i++; continue; }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { depth = Math.Max(0, depth - 1); i++; continue; }
            if (depth > 0) continue;
            if (string.CompareOrdinal(text, i, op, 0, op.Length) != 0) continue;
            // A single ">"/"<" must not be the first char of ">="/"<=" (handled by the longer op).
            if (op.Length == 1 && i + 1 < text.Length && text[i + 1] == '=') continue;
            return i;
        }
        return -1;
    }

    [GeneratedRegex(@"\{\{\s*([\w.\-]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\$\{\s*([\w.\-]+)\s*\}")]
    private static partial Regex DollarPlaceholderRegex();
}
