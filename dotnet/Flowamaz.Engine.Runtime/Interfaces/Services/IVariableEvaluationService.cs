namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Resolves <c>{{ name }}</c> / <c>{{ variables.name }}</c> / <c>{{ node-id.output.field }}</c> placeholders in node config and edge conditions against an
/// instance's <c>WorkflowVariable</c> rows. Phase 2 keeps this to simple interpolation plus
/// equality/inequality comparisons — richer expressions arrive later.
/// </summary>
public interface IVariableEvaluationService
{
    /// <summary>Replaces every <c>{{ name }}</c> in <paramref name="template"/> with the variable value (missing → empty).</summary>
    Task<string> InterpolateAsync(Guid instanceId, string template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="InterpolateAsync"/> but returns JSON: when the whole template is a single
    /// placeholder the referenced value keeps its JSON type (number, object, array); otherwise the
    /// interpolated text is returned as a JSON string. Used for connector inputs.
    /// </summary>
    Task<System.Text.Json.JsonElement> InterpolateValueAsync(Guid instanceId, string template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates an edge condition. Null/empty is unconditional (true). Supports
    /// <c>{{ a }} == "x"</c> and <c>!=</c>; a bare interpolated value is truthy when non-empty.
    /// </summary>
    Task<bool> EvaluateConditionAsync(Guid instanceId, string? condition, CancellationToken cancellationToken = default);
}
