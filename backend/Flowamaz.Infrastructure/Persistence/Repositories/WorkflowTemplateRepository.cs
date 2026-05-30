using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for workflow templates. Reads return only active, approved templates for
/// the public gallery; install_count is incremented atomically at the DB level.</summary>
public sealed class WorkflowTemplateRepository(FlowAmazDbContext db) : IWorkflowTemplateRepository
{
    public async Task<List<WorkflowTemplate>> ListAsync(string? category, string? search, CancellationToken cancellationToken = default)
    {
        var query = db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.ReviewStatus == "approved");

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(t => t.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Description.ToLower().Contains(term));
        }

        return await query.OrderByDescending(t => t.InstallCount).ThenBy(t => t.Name).ToListAsync(cancellationToken);
    }

    public Task<WorkflowTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        db.WorkflowTemplates.AnyAsync(t => t.Slug == slug, cancellationToken);

    public async Task AddAsync(WorkflowTemplate template, CancellationToken cancellationToken = default) =>
        await db.WorkflowTemplates.AddAsync(template, cancellationToken);

    public Task IncrementInstallCountAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkflowTemplates
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.InstallCount, t => t.InstallCount + 1), cancellationToken);
}
