using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Git;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Workflow-definition CRUD + publish. YAML is validated through <see cref="SfgParser"/> at publish
/// time only — drafts are saved as-is so AI-generated YAML can be refined before publishing.
/// All operations are workspace-scoped. Versioning, the workflow limit and lifecycle events go through the host
/// hooks (<see cref="IWorkflowVersionStore"/>, <see cref="IWorkflowAdmission"/>, <see cref="IWorkflowEventSink"/>).
/// </summary>
public sealed class WorkflowService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly IWorkflowVersionStore _store;
    private readonly IWorkflowAdmission _admission;
    private readonly IWorkflowEventSink _events;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowInstanceRepository instances,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        IWorkflowVersionStore store,
        IWorkflowAdmission admission,
        IWorkflowEventSink events,
        ILogger<WorkflowService> logger)
    {
        _definitions = definitions;
        _versions = versions;
        _instances = instances;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _store = store;
        _admission = admission;
        _events = events;
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

        if (string.IsNullOrWhiteSpace(request.YamlContent) && request.CreatedByMethod != WorkflowCreatedByMethod.Canvas)
            throw new InvalidOperationException(
                $"YamlContent cannot be empty for creation method {request.CreatedByMethod}. Ensure the generation step completed before calling CreateAsync.");

        // The host's workflow limit, if any (the product: the edition's workflow count, prompt 05-07).
        await _admission.EnsureCanAddAsync(workspaceId, ct);

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
        SyncFromYaml(definition);

        await _definitions.AddAsync(definition, ct);

        // Every save is versioned; the reference becomes CurrentVersion (in the product a Git commit SHA, so the
        // exact YAML can be reconstructed for any historical instance, FUNCTIONAL.md §2.6).
        var saved = await _store.SaveAsync(new WorkflowSave(
            workspaceId, definition.Id, definition.YamlContent ?? string.Empty, $"Create {definition.Name}", null), ct);
        if (saved is not null) definition.CurrentVersion = saved.VersionRef;

        await _unitOfWork.SaveChangesAsync(ct);
        await _events.WorkflowChangedAsync(new WorkflowChangedEvent(workspaceId, definition.Id, definition.Name, "created", createdBy), ct);

        _logger.LogInformation("WorkflowService.CreateAsync exit workflow={WorkflowId} commit={Commit}", definition.Id, definition.CurrentVersion);
        return ToResponse(definition);
    }

    public async Task<WorkflowDefinitionResponse?> GetByIdAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        return definition is null ? null : ToResponse(definition);
    }

    /// <param name="author">Who saved; the commit author (the system author when null).</param>
    /// <param name="coAuthors">Others who edited in a shared editing session since the last save (Co-authored-by trailers).</param>
    public async Task<WorkflowDefinitionResponse?> UpdateAsync(
        Guid workspaceId, Guid id, string yamlContent, long? slaThresholdMs = null, CancellationToken ct = default,
        CommitAuthor? author = null, IReadOnlyList<CommitAuthor>? coAuthors = null)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (definition is null) return null;

        definition.YamlContent = yamlContent;
        definition.SlaThresholdMs = slaThresholdMs;
        SyncFromYaml(definition);

        var saved = await _store.SaveAsync(new WorkflowSave(
            workspaceId, definition.Id, yamlContent ?? string.Empty,
            CommitMessage($"Update {definition.Name}", author, coAuthors),
            new CommitAuthor(Clean(author?.Name), Clean(author?.Email))), ct);
        if (saved is not null) definition.CurrentVersion = saved.VersionRef;

        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(definition);
    }

    /// <summary>Commit message with one <c>Co-authored-by</c> trailer per collaborator (Git's convention).</summary>
    internal static string CommitMessage(string subject, CommitAuthor? author, IReadOnlyList<CommitAuthor>? coAuthors)
    {
        var trailers = (coAuthors ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Email)
                && !string.Equals(c.Email, author?.Email, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => c.Email.ToLowerInvariant())
            .Select(c => $"Co-authored-by: {Clean(c.Name)} <{Clean(c.Email)}>")
            .ToList();
        return trailers.Count == 0 ? subject : $"{subject}\n\n{string.Join("\n", trailers)}\n";
    }

    /// <summary>Names and emails go into commit headers and trailers: no line breaks or angle brackets.</summary>
    private static string Clean(string? value) =>
        new string((value ?? string.Empty).Where(ch => ch is not ('\r' or '\n' or '<' or '>')).ToArray()).Trim();

    /// <summary>
    /// Keeps the overview's trigger type and description in step with the YAML on every save (they were
    /// only set at publish / never). Schedule validity is still enforced at publish.
    /// </summary>
    private static void SyncFromYaml(WorkflowDefinition definition)
    {
        definition.TriggerType = Scheduling.WorkflowTriggerReader.Read(definition.YamlContent).Type;
        var description = Scheduling.WorkflowTriggerReader.ReadDescription(definition.YamlContent);
        if (description is not null) definition.Description = description;
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

    /// <summary>
    /// Deletes a Draft workflow (soft delete). Published workflows cannot be deleted — they are
    /// archived instead — so this throws <see cref="WorkflowNotDraftException"/> (422) for any
    /// non-Draft status. Missing/wrong-workspace ids throw <see cref="WorkflowNotFoundException"/> (404).
    /// </summary>
    public async Task DeleteAsync(Guid workflowId, Guid workspaceId, Guid deletedByUserId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowService.DeleteAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);

        var definition = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct);
        if (definition is null)
        {
            _logger.LogWarning("WorkflowService.DeleteAsync not-found workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw new WorkflowNotFoundException(workflowId);
        }

        if (definition.Status != WorkflowStatus.Draft)
        {
            _logger.LogWarning("WorkflowService.DeleteAsync rejected workflow={WorkflowId} status={Status}", workflowId, definition.Status);
            throw new WorkflowNotDraftException();
        }

        definition.IsDeleted = true;
        definition.DeletedAt = DateTime.UtcNow;
        definition.UpdatedBy = deletedByUserId;
        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        await _events.WorkflowChangedAsync(new WorkflowChangedEvent(workspaceId, definition.Id, definition.Name, "deleted", deletedByUserId), ct);

        _logger.LogInformation("WorkflowService.DeleteAsync exit workflow={WorkflowId}", workflowId);
    }

    /// <summary>
    /// Archives a published workflow: it keeps its versions, runs and history, but can't be run (manual,
    /// webhook, schedule or API triggers are refused because it is no longer Published), is hidden from the
    /// web workflow list unless "Show archived" is on, and doesn't count toward the edition's workflow limit. Runs already in
    /// progress finish. Drafts are deleted instead (<see cref="DeleteAsync"/>).
    /// </summary>
    public async Task<WorkflowDefinitionResponse> ArchiveAsync(Guid workspaceId, Guid id, Guid userId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowService.ArchiveAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct)
            ?? throw new WorkflowNotFoundException(id);
        if (definition.Status == WorkflowStatus.Archived) return ToResponse(definition); // already: no-op
        if (definition.Status != WorkflowStatus.Published) throw WorkflowArchiveException.NotPublished();

        definition.Status = WorkflowStatus.Archived;
        definition.UpdatedBy = userId;
        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        await _events.WorkflowChangedAsync(new WorkflowChangedEvent(workspaceId, definition.Id, definition.Name, "archived", userId), ct);

        _logger.LogInformation("WorkflowService.ArchiveAsync exit workflow={WorkflowId}", id);
        return ToResponse(definition);
    }

    /// <summary>
    /// Returns an archived workflow to Published, on the production version it had. It counts toward the
    /// workflow limit again, so this is refused (402) when the workspace is already at the limit.
    /// </summary>
    public async Task<WorkflowDefinitionResponse> UnarchiveAsync(Guid workspaceId, Guid id, Guid userId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowService.UnarchiveAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);
        var definition = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, ct)
            ?? throw new WorkflowNotFoundException(id);
        if (definition.Status != WorkflowStatus.Archived) throw WorkflowArchiveException.NotArchived();

        await _admission.EnsureCanAddAsync(workspaceId, ct);

        definition.Status = WorkflowStatus.Published;
        definition.UpdatedBy = userId;
        _definitions.Update(definition);
        await _unitOfWork.SaveChangesAsync(ct);
        await _events.WorkflowChangedAsync(new WorkflowChangedEvent(workspaceId, definition.Id, definition.Name, "unarchived", userId), ct);

        _logger.LogInformation("WorkflowService.UnarchiveAsync exit workflow={WorkflowId}", id);
        return ToResponse(definition);
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
        // Publishing would bring it back without the workflow-cap check that unarchiving does.
        if (definition.Status == WorkflowStatus.Archived) throw WorkflowArchiveException.PublishWhileArchived();

        await _parser.ParseAsync(definition.YamlContent, ct); // never publish an invalid graph

        // Record the trigger type so the schedule job can find scheduled workflows, and refuse to
        // publish a schedule that could never fire.
        var trigger = Scheduling.WorkflowTriggerReader.Read(definition.YamlContent);
        if (trigger.Type == WorkflowTriggerType.Schedule
            && !Scheduling.CronSchedule.TryParse(trigger.Cron, trigger.TimeZone, out _, out var scheduleError))
            throw new InvalidScheduleException(scheduleError);
        definition.TriggerType = trigger.Type;

        // Demote the current production version, if any.
        var current = await _versions.GetProductionAsync(id, workspaceId, ct);
        if (current is not null)
        {
            current.IsProduction = false;
            _versions.Update(current);
        }

        // Version the published YAML, then tag it as a release (the product: a Git commit and tag, FUNCTIONAL.md
        // §2.6). The reference is immutable; instances pin to the version row, which keeps its own copy of the YAML.
        var saved = await _store.SaveAsync(new WorkflowSave(
            workspaceId, id, definition.YamlContent, $"Published {definition.Name}", null), ct);
        var commitSha = saved?.VersionRef ?? Guid.NewGuid().ToString("N");
        var branchName = saved?.BranchName ?? "main";

        var existingVersions = await _versions.GetForDefinitionAsync(id, workspaceId, ct);
        var versionNumber = (existingVersions?.Count ?? 0) + 1;
        var tagName = await _store.TagReleaseAsync(workspaceId, id, versionNumber, ct);

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

        await _events.WorkflowChangedAsync(
            new WorkflowChangedEvent(workspaceId, id, definition.Name, "published", publishedBy, versionNumber, commitSha), ct);

        _logger.LogInformation("WorkflowService.PublishAsync exit workflow={WorkflowId} version={VersionId}", id, version.Id);
        return ToVersionResponse(version);
    }

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) => new(
        d.Id, d.WorkspaceId, d.Name, d.Slug, d.Description, d.YamlContent, d.NlDescription,
        d.CreatedByMethod, d.Status, d.CurrentVersion, d.HealthScore, d.TriggerType, d.SlaThresholdMs, d.CreatedAt, d.UpdatedAt);

    private static WorkflowVersionResponse ToVersionResponse(WorkflowVersion v) => new(
        v.Id, v.WorkflowDefinitionId, v.CommitSha, v.TagName, v.BranchName, v.Message, v.IsProduction, v.CreatedAt);
}
