using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Runtime variables for an instance. Add/Update stage; the caller commits.</summary>
public interface IWorkflowVariableRepository
{
    Task<List<WorkflowVariable>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
    /// <summary>
    /// The instance's variables as stored (name → JSON text), read without tracking: what was committed,
    /// not what this scope has staged. Used to resolve placeholders and conditions.
    /// </summary>
    Task<Dictionary<string, string>> GetStoredValuesAsync(Guid instanceId, CancellationToken cancellationToken = default);
    Task<WorkflowVariable?> GetByNameAsync(Guid instanceId, string name, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowVariable variable, CancellationToken cancellationToken = default);
    void Update(WorkflowVariable variable);
}
