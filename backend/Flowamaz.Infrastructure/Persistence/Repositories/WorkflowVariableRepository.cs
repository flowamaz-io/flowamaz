using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowVariableRepository(FlowAmazDbContext db) : IWorkflowVariableRepository
{
    public Task<List<WorkflowVariable>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVariables.Where(v => v.InstanceId == instanceId).ToListAsync(cancellationToken);

    public Task<WorkflowVariable?> GetByNameAsync(Guid instanceId, string name, CancellationToken cancellationToken = default) =>
        db.WorkflowVariables.FirstOrDefaultAsync(v => v.InstanceId == instanceId && v.Name == name, cancellationToken);

    public async Task AddAsync(WorkflowVariable variable, CancellationToken cancellationToken = default) =>
        await db.WorkflowVariables.AddAsync(variable, cancellationToken);

    public void Update(WorkflowVariable variable) => db.WorkflowVariables.Update(variable);
}
