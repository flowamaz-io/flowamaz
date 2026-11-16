using System.Text.RegularExpressions;
using Flowamaz.Core.Interfaces.Repositories;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Keeps sensitive variables out of AI prompts. Used by every AI step (the open runtime's and the
/// product's): the placeholders of sensitive variables are replaced with <see cref="Redacted"/> before the
/// prompt is filled, so the value is never substituted in the first place.
/// </summary>
public static class AiPromptRedaction
{
    public const string Redacted = "[SENSITIVE_REDACTED]";

    /// <summary>
    /// The run's sensitive variable names: those stored as sensitive (encrypted), plus any the host names.
    /// </summary>
    public static async Task<HashSet<string>> SensitiveNamesAsync(
        IWorkflowVariableRepository variables, Guid instanceId, IReadOnlySet<string>? alsoSensitive, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(alsoSensitive ?? new HashSet<string>(), StringComparer.Ordinal);
        foreach (var variable in await variables.GetForInstanceAsync(instanceId, cancellationToken))
        {
            if (variable.IsSensitive) names.Add(variable.Name);
        }
        return names;
    }

    /// <summary>
    /// Replaces <c>{{ name }}</c> and <c>{{ variables.name }}</c> (any spacing, dotted paths under it too)
    /// for each sensitive name.
    /// </summary>
    public static string Redact(string template, IReadOnlyCollection<string> sensitive)
    {
        foreach (var name in sensitive)
        {
            template = Regex.Replace(template, @"\{\{\s*(variables\.)?" + Regex.Escape(name) + @"(\.[^}\s]*)?\s*\}\}", Redacted);
        }
        return template;
    }
}
