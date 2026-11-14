using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Resolves <c>{{ name }}</c> placeholders against an instance's variables and evaluates simple
/// edge conditions (==, !=, or truthiness). Phase 2 scope — no arithmetic or nested expressions.
/// </summary>
public sealed class VariableEvaluationService : IVariableEvaluationService
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
        var result = ConditionEvaluator.Interpolate(template, vars);

        _logger.LogDebug("VariableEvaluationService.InterpolateAsync exit instance={InstanceId}", instanceId);
        return result;
    }

    public async Task<JsonElement> InterpolateValueAsync(Guid instanceId, string template, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("VariableEvaluationService.InterpolateValueAsync enter instance={InstanceId}", instanceId);
        if (ConditionEvaluator.SolePlaceholderPath(template ?? string.Empty) is { } path && template!.Trim().Length == template.Length)
        {
            // The whole value is one placeholder: keep its JSON type (number, object, array…).
            var vars = await LoadVariablesAsync(instanceId, cancellationToken);
            var resolved = ConditionEvaluator.Resolve(vars, path);
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
            // Same rules as the Workflow Twin simulator (ConditionEvaluator), so simulated runs route like real ones.
            var vars = await LoadVariablesAsync(instanceId, cancellationToken);
            return ConditionEvaluator.Evaluate(condition, vars);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "VariableEvaluationService.EvaluateConditionAsync error instance={InstanceId} condition={Condition}",
                instanceId, condition);
            throw;
        }
    }

    internal static bool Compare(string left, string right, string op) => ConditionEvaluator.Compare(left, right, op);

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
    internal static JsonElement ParseStored(string json)
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

    /// <inheritdoc cref="ConditionEvaluator.Resolve"/>
    internal static JsonElement? Resolve(IReadOnlyDictionary<string, JsonElement> vars, string path) =>
        ConditionEvaluator.Resolve(vars, path);
}
