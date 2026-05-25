using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Append-only access to the event log. Insert and read only — there is intentionally no update or
/// delete path so the audit trail stays immutable (prompt 02-01). The caller commits the staged
/// insert via the unit of work.
/// </summary>
public sealed class WorkflowEventRepository(FlowAmazDbContext db) : IWorkflowEventRepository
{
    public async Task AppendAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default) =>
        await db.WorkflowEvents.AddAsync(workflowEvent, cancellationToken);

    public Task<List<WorkflowEvent>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowEvents.AsNoTracking()
            .Where(e => e.InstanceId == instanceId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(cancellationToken);

    public async Task<long> GetNextSequenceNumberAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var max = await db.WorkflowEvents
            .Where(e => e.InstanceId == instanceId)
            .Select(e => (long?)e.SequenceNumber)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }
}
