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
        {
            var name = m.Groups[1].Value;
            return vars.TryGetValue(name, out var value) ? value : string.Empty;
        });

        _logger.LogDebug("VariableEvaluationService.InterpolateAsync exit instance={InstanceId}", instanceId);
        return result;
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

    private async Task<Dictionary<string, string>> LoadVariablesAsync(Guid instanceId, CancellationToken ct)
    {
        var rows = await _db.WorkflowVariables.AsNoTracking()
            .Where(v => v.InstanceId == instanceId)
            .Select(v => new { v.Name, v.Value })
            .ToListAsync(ct);

        var dict = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows) dict[row.Name] = ExtractScalar(row.Value);
        return dict;
    }

    // Variables are stored as jsonb. A JSON string unwraps to its text; anything else uses raw JSON.
    private static string ExtractScalar(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? string.Empty
                : doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    [GeneratedRegex(@"\{\{\s*([\w.]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
