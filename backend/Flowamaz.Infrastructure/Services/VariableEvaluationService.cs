using System.Text.Json;
using System.Text.RegularExpressions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Resolves <c>{{ name }}</c> placeholders against an instance's variables and evaluates simple
/// edge conditions (==, !=, or truthiness). Phase 2 scope — no arithmetic or nested expressions.
/// </summary>
public sealed partial class VariableEvaluationService : IVariableEvaluationService
{
    private readonly FlowAmazDbContext _db;
    private readonly ILogger<VariableEvaluationService> _logger;

    public VariableEvaluationService(FlowAmazDbContext db, ILogger<VariableEvaluationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> InterpolateAsync(Guid instanceId, string template, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("VariableEvaluationService.InterpolateAsync enter instance={InstanceId}", instanceId);
        if (string.IsNullOrEmpty(template)) return template;

        var vars = await LoadVariablesAsync(instanceId, cancellationToken);
        var result = PlaceholderRegex().Replace(template, m =>
            Resolve(vars, m.Groups[1].Value) is { } value ? ToText(value) : string.Empty);

        _logger.LogDebug("VariableEvaluationService.InterpolateAsync exit instance={InstanceId}", instanceId);
        return result;
    }

    public async Task<JsonElement> InterpolateValueAsync(Guid instanceId, string template, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("VariableEvaluationService.InterpolateValueAsync enter instance={InstanceId}", instanceId);
        var match = PlaceholderRegex().Match(template ?? string.Empty);
        if (match.Success && match.Index == 0 && match.Length == template!.Length)
        {
            // The whole value is one placeholder: keep its JSON type (number, object, array…).
            var vars = await LoadVariablesAsync(instanceId, cancellationToken);
            var resolved = Resolve(vars, match.Groups[1].Value);
            return resolved ?? JsonSerializer.SerializeToElement((string?)null);
        }

        return JsonSerializer.SerializeToElement(await InterpolateAsync(instanceId, template ?? string.Empty, cancellationToken));
    }

    public async Task<bool> EvaluateConditionAsync(Guid instanceId, string? condition, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        _logger.LogDebug("VariableEvaluationService.EvaluateConditionAsync enter instance={InstanceId}", instanceId);

        try
        {
            foreach (var op in new[] { "==", "!=" })
            {
                var idx = condition.IndexOf(op, StringComparison.Ordinal);
                if (idx < 0) continue;

                var left = Unquote((await InterpolateAsync(instanceId, condition[..idx].Trim(), cancellationToken)).Trim());
                var right = Unquote((await InterpolateAsync(instanceId, condition[(idx + op.Length)..].Trim(), cancellationToken)).Trim());
                var equal = string.Equals(left, right, StringComparison.Ordinal);
                return op == "==" ? equal : !equal;
            }

            // No operator → truthiness of the interpolated value.
            var value = (await InterpolateAsync(instanceId, condition, cancellationToken)).Trim();
            return value.Length > 0
                   && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                   && value != "0";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "VariableEvaluationService.EvaluateConditionAsync error instance={InstanceId} condition={Condition}",
                instanceId, condition);
            throw;
        }
    }

    private async Task<Dictionary<string, JsonElement>> LoadVariablesAsync(Guid instanceId, CancellationToken ct)
    {
        var rows = await _db.WorkflowVariables.AsNoTracking()
            .Where(v => v.InstanceId == instanceId)
            .Select(v => new { v.Name, v.Value })
            .ToListAsync(ct);

        var dict = new Dictionary<string, JsonElement>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows) dict[row.Name] = ParseStored(row.Value);
        return dict;
    }

    // Variables are stored as jsonb; a value that is not valid JSON is treated as a plain string.
    private static JsonElement ParseStored(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonSerializer.SerializeToElement(string.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json);
        }
    }

    /// <summary>
    /// Resolves a placeholder path. Accepts the spec form <c>variables.amount</c>, plain <c>amount</c>,
    /// and paths into JSON values such as <c>create-issue.output.number</c> or <c>rows.0.id</c>
    /// (longest matching variable name wins, the rest navigates object keys / array indexes).
    /// </summary>
    internal static JsonElement? Resolve(IReadOnlyDictionary<string, JsonElement> vars, string path)
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

    // A JSON string unwraps to its text; anything else uses raw JSON.
    private static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    [GeneratedRegex(@"\{\{\s*([\w.\-]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
