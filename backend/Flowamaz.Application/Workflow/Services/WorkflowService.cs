using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Git;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Workflow-definition CRUD + publish. YAML is validated through <see cref="SfgParser"/> at publish
/// time only — drafts are saved as-is so AI-generated YAML can be refined before publishing.
/// All operations are workspace-scoped.
/// </summary>
public sealed class WorkflowService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly IWorkspaceGitService _git;
    private readonly IEditionService _edition;
    private readonly ILogger<WorkflowService> _logger;
    private readonly IAuditService? _audit;

    public WorkflowService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowInstanceRepository instances,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        IWorkspaceGitService git,
        IEditionService edition,
        ILogger<WorkflowService> logger,
        IAuditService? audit = null)
    {
        _definitions = definitions;
        _versions = versions;
        _instances = instances;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _git = git;
        _edition = edition;
        _logger = logger;
        _audit = audit;
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

        if (string.IsNullOrWhiteSpace(request.YamlContent) && request.CreatedByMethod != WorkflowCreatedByMethod.Canvas)
            throw new InvalidOperationException(
                $"YamlContent cannot be empty for creation method {request.CreatedByMethod}. Ensure the generation step completed before calling CreateAsync.");

        // Edition gate (prompt 05-07): Community edition caps the workflow count.
        await _edition.EnsureWithinLimitAsync(workspaceId, LimitType.WorkflowCount, ct);

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
            SlaThresholdMs = request.SlaThresholdMs,
            CreatedBy = createdBy,
        };

        await _definitions.AddAsync(definition, ct);

        // Git-native versioning: every save is a commit. The commit SHA becomes CurrentVersion so the
        // exact YAML can be reconstructed for any historical instance (FUNCTIONAL.md §2.6).
        var commit = await _git.CommitWorkflowAsync(
            workspaceId, definition.Id, definition.YamlContent ?? string.Empty,
            $"Create {definition.Name}", string.Empty, string.Empty, ct);
        if (commit is not null) definition.CurrentVersion = commit.CommitSha;

        await _unitOfWork.SaveChangesAsync(ct);

        _audit?.RecordAsync(new AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = createdBy,
            ActorType = "user",
            EventType = "workflow.created",
            ResourceType = "workflow",
            ResourceId = definition.Id,
            ResourceLabel = definition.Name,
            Action = "created",
        }, ct);

        _logger.LogInformation("WorkflowService.CreateAsync exit workflow={WorkflowId} commit={Commit}", definition.Id, definition.CurrentVersion);
        return ToResponse(definition);
    }

    public async Task<WorkflowDefinitionResponse?> GetByIdAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        return definition is null ? null : ToResponse(definition);
    }

    public async Task<WorkflowDefinitionResponse?> UpdateAsync(
        Guid workspaceId, Guid id, string yamlContent, long? slaThresholdMs = null, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return null;

        definition.YamlContent = yamlContent;
        definition.SlaThresholdMs = slaThresholdMs;

        var commit = await _git.CommitWorkflowAsync(
            workspaceId, definition.Id, yamlContent ?? string.Empty,
            $"Update {definition.Name}", string.Empty, string.Empty, ct);
        if (commit is not null) definition.CurrentVersion = commit.CommitSha;

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

        // Git-native versioning (FUNCTIONAL.md §2.6): commit the published YAML, then tag it as a
        // release. The commit SHA is the immutable reference instances pin to forever.
        var commit = await _git.CommitWorkflowAsync(
            workspaceId, id, definition.YamlContent,
            $"Published {definition.Name}", string.Empty, string.Empty, ct);
        var commitSha = commit?.CommitSha ?? Guid.NewGuid().ToString("N");
        var branchName = commit?.BranchName ?? "main";

        var existingVersions = await _versions.GetForDefinitionAsync(id, workspaceId, ct);
        var versionNumber = (existingVersions?.Count ?? 0) + 1;
        var tagName = await _git.PublishWorkflowAsync(workspaceId, id, versionNumber, ct);

        var version = new WorkflowVersion
        {
            WorkspaceId = workspaceId,
            WorkflowDefinitionId = id,
            CommitSha = commitSha,
            TagName = tagName,
            BranchName = branchName,
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

        _audit?.RecordAsync(new AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = publishedBy,
            ActorType = "user",
            EventType = "workflow.published",
            ResourceType = "workflow",
            ResourceId = id,
            ResourceLabel = definition.Name,
            Action = "published",
            Metadata = new { version = versionNumber, commit = commitSha },
        }, ct);

        _logger.LogInformation("WorkflowService.PublishAsync exit workflow={WorkflowId} version={VersionId}", id, version.Id);
        return ToVersionResponse(version);
    }

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) => new(
        d.Id, d.WorkspaceId, d.Name, d.Slug, d.Description, d.YamlContent, d.NlDescription,
        d.CreatedByMethod, d.Status, d.CurrentVersion, d.HealthScore, d.TriggerType, d.SlaThresholdMs, d.CreatedAt, d.UpdatedAt);

    private static WorkflowVersionResponse ToVersionResponse(WorkflowVersion v) => new(
        v.Id, v.WorkflowDefinitionId, v.CommitSha, v.TagName, v.BranchName, v.Message, v.IsProduction, v.CreatedAt);
}
