using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowVariableRepository(EngineDbContext db) : IWorkflowVariableRepository
{
    public Task<List<WorkflowVariable>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVariables.Where(v => v.InstanceId == instanceId).ToListAsync(cancellationToken);

    public async Task<Dictionary<string, string>> GetStoredValuesAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkflowVariables.AsNoTracking()
            .Where(v => v.InstanceId == instanceId)
            .Select(v => new { v.Name, v.Value })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows) result[row.Name] = row.Value;
        return result;
    }

    public Task<WorkflowVariable?> GetByNameAsync(Guid instanceId, string name, CancellationToken cancellationToken = default) =>
        db.WorkflowVariables.FirstOrDefaultAsync(v => v.InstanceId == instanceId && v.Name == name, cancellationToken);

    public async Task AddAsync(WorkflowVariable variable, CancellationToken cancellationToken = default) =>
        await db.WorkflowVariables.AddAsync(variable, cancellationToken);

    public void Update(WorkflowVariable variable) => db.WorkflowVariables.Update(variable);
}
