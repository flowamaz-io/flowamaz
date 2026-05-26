using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Workflow-definition CRUD + publish. YAML is validated through <see cref="SfgParser"/> before any
/// create/update is saved (invalid YAML surfaces as a 422 SfgParseException). Publishing snapshots
/// the YAML into a production <see cref="WorkflowVersion"/>. All operations are workspace-scoped.
/// </summary>
public sealed class WorkflowService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowInstanceRepository instances,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        ILogger<WorkflowService> logger)
    {
        _definitions = definitions;
        _versions = versions;
        _instances = instances;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _logger = logger;
    }

    public async Task<List<WorkflowDefinitionListItem>> ListAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var definitions = await _definitions.GetForWorkspaceAsync(workspaceId, ct);
        return definitions
            .Select(d => new WorkflowDefinitionListItem(d.Id, d.Name, d.Slug, d.Status, d.HealthScore, d.UpdatedAt))
            .ToList();
    }

    public async Task<WorkflowDefinitionResponse> CreateAsync(
        Guid workspaceId, Guid createdBy, CreateWorkflowDefinitionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowService.CreateAsync enter workspace={WorkspaceId} slug={Slug}", workspaceId, request.Slug);

        // Validate the graph before persisting — throws SfgParseException (422) on invalid YAML.
        await _parser.ParseAsync(request.YamlContent, ct);

        if (await _definitions.SlugExistsInWorkspaceAsync(workspaceId, request.Slug, ct))
        {
            throw new WorkflowSlugExistsException(request.Slug);
        }

        var definition = new WorkflowDefinition
        {
            WorkspaceId = workspaceId,
            Name = request.Name,
            Slug = request.Slug,
            YamlContent = request.YamlContent,
            NlDescription = request.NlDescription,
            CreatedByMethod = request.CreatedByMethod,
            Status = WorkflowStatus.Draft,
            CurrentVersion = "draft",
            CreatedBy = createdBy,
        };

        await _definitions.AddAsync(definition, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("WorkflowService.CreateAsync exit workflow={WorkflowId}", definition.Id);
        return ToResponse(definition);
    }

    public async Task<WorkflowDefinitionResponse?> GetByIdAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        return definition is null ? null : ToResponse(definition);
    }

    public async Task<WorkflowDefinitionResponse?> UpdateAsync(
        Guid workspaceId, Guid id, string yamlContent, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return null;

        await _parser.ParseAsync(yamlContent, ct); // validate before save

        definition.YamlContent = yamlContent;
        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(definition);
    }

    public async Task<bool> SoftDeleteAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return false;

        if (await _instances.HasActiveInstancesAsync(id, workspaceId, ct))
        {
            throw new WorkflowHasActiveInstancesException();
        }

        definition.IsDeleted = true;
        definition.DeletedAt = DateTime.UtcNow;
        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<WorkflowVersionResponse>?> GetVersionsAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return null;

        var versions = await _versions.GetForDefinitionAsync(id, workspaceId, ct);
        return versions.Select(ToVersionResponse).ToList();
    }

    public async Task<WorkflowVersionResponse?> PublishAsync(Guid workspaceId, Guid id, Guid publishedBy, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowService.PublishAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);

        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return null;

        await _parser.ParseAsync(definition.YamlContent, ct); // never publish an invalid graph

        // Demote the current production version, if any.
        var current = await _versions.GetProductionAsync(id, workspaceId, ct);
        if (current is not null)
        {
            current.IsProduction = false;
            _versions.Update(current);
        }

        // Phase 2 placeholder commit SHA — real Git versioning arrives in Phase 5 (WorkspaceGitService).
        var commitSha = Guid.NewGuid().ToString("N");
        var version = new WorkflowVersion
        {
            WorkspaceId = workspaceId,
            WorkflowDefinitionId = id,
            CommitSha = commitSha,
            BranchName = "main",
            YamlContent = definition.YamlContent,
            Message = $"Published {definition.Name}",
            IsProduction = true,
            CreatedBy = publishedBy,
        };
        await _versions.AddAsync(version, ct);

        definition.Status = WorkflowStatus.Published;
        definition.CurrentVersion = commitSha;
        _definitions.Update(definition);

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("WorkflowService.PublishAsync exit workflow={WorkflowId} version={VersionId}", id, version.Id);
        return ToVersionResponse(version);
    }

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) => new(
        d.Id, d.WorkspaceId, d.Name, d.Slug, d.Description, d.YamlContent, d.NlDescription,
        d.CreatedByMethod, d.Status, d.CurrentVersion, d.HealthScore, d.TriggerType, d.CreatedAt, d.UpdatedAt);

    private static WorkflowVersionResponse ToVersionResponse(WorkflowVersion v) => new(
        v.Id, v.WorkflowDefinitionId, v.CommitSha, v.TagName, v.BranchName, v.Message, v.IsProduction, v.CreatedAt);
}
