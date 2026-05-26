using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;

namespace Flowamaz.Application.Analytics;

/// <summary>Lists and acknowledges Process-Intelligence insights. Workspace-scoped.</summary>
public sealed class InsightService
{
    private readonly IWorkflowInsightRepository _insights;
    private readonly IUnitOfWork _unitOfWork;

    public InsightService(IWorkflowInsightRepository insights, IUnitOfWork unitOfWork)
    {
        _insights = insights;
        _unitOfWork = unitOfWork;
    }

    public async Task<List<InsightResponse>> ListAsync(
        Guid workspaceId, InsightSeverity? severity, bool? acknowledged, Guid? workflowDefinitionId, CancellationToken ct = default)
    {
        var insights = await _insights.ListAsync(workspaceId, severity, acknowledged, workflowDefinitionId, ct);
        return insights.Select(ToResponse).ToList();
    }

    public async Task<bool> AcknowledgeAsync(Guid workspaceId, Guid id, Guid userId, CancellationToken ct = default)
    {
        var insight = await _insights.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (insight is null) return false;

        insight.IsAcknowledged = true;
        insight.AcknowledgedBy = userId;
        insight.AcknowledgedAt = DateTime.UtcNow;
        _insights.Update(insight);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static InsightResponse ToResponse(WorkflowInsight i) => new(
        i.Id, i.WorkflowDefinitionId, i.InstanceId, i.InsightType.ToString(), i.Severity.ToString(),
        i.Message, i.Data, i.IsAcknowledged, i.AcknowledgedAt, i.ExpiresAt, i.CreatedAt);
}
