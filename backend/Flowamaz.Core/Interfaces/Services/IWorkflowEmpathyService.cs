using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Analyses a workflow definition for empathy issues — deterministic graph analysis, no AI calls.
/// </summary>
public interface IWorkflowEmpathyService
{
    Task<EmpathyAnalysis> AnalyseAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Analyse the provided YAML directly without loading from the database.</summary>
    Task<EmpathyAnalysis> AnalyseYamlAsync(string yaml, Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default);
}
