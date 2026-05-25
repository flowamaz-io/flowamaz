using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workspace.Services;

/// <summary>
/// Provisions and manages workspaces. Creation is atomic: the workspace, its three environments
/// (Dev/Staging/Production) and the creator's Admin membership all commit together or not at all.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IWorkspaceMemberRepository _memberRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IWorkspaceRepository workspaceRepository,
        IWorkspaceMemberRepository memberRepository,
        IUnitOfWork unitOfWork,
        ILogger<WorkspaceService> logger)
    {
        _workspaceRepository = workspaceRepository;
        _memberRepository = memberRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Core.Entities.Workspaces.Workspace> CreateWorkspaceAsync(
        Guid orgId, string name, string slug, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var normalisedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        _logger.LogDebug(
            "WorkspaceService.CreateWorkspaceAsync enter orgId={OrgId} slug={Slug} createdBy={UserId}",
            orgId, normalisedSlug, createdByUserId);

        try
        {
            if (await _workspaceRepository.SlugExistsInOrgAsync(orgId, normalisedSlug, cancellationToken))
            {
                throw new SlugAlreadyExistsException(normalisedSlug);
            }

            var workspace = new Core.Entities.Workspaces.Workspace
            {
                OrgId = orgId,
                Name = name,
                Slug = normalisedSlug,
                Settings = new WorkspaceSettings(),
            };

            var environments = Enum.GetValues<WorkspaceEnvironmentType>()
                .Select(type => new WorkspaceEnvironment { WorkspaceId = workspace.Id, Name = type })
                .ToList();

            var adminMember = new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                OrgUserId = createdByUserId,
                Role = WorkspaceRole.Admin,
                IsActive = true,
            };

            await using (var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    await _workspaceRepository.AddAsync(workspace, cancellationToken);
                    foreach (var environment in environments)
                    {
                        await _workspaceRepository.AddEnvironmentAsync(environment, cancellationToken);
                    }
                    await _memberRepository.AddAsync(adminMember, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            _logger.LogInformation(
                "WorkspaceService.CreateWorkspaceAsync exit workspaceId={WorkspaceId} orgId={OrgId} environments={EnvCount} adminUserId={UserId}",
                workspace.Id, orgId, environments.Count, createdByUserId);
            return workspace;
        }
        catch (SlugAlreadyExistsException)
        {
            // Expected business outcome — surfaced to the caller without an error log.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.CreateWorkspaceAsync error orgId={OrgId} slug={Slug}", orgId, normalisedSlug);
            throw;
        }
    }

    public async Task<Core.Entities.Workspaces.Workspace?> GetByIdAsync(
        Guid workspaceId, Guid orgId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceService.GetByIdAsync enter workspaceId={WorkspaceId} orgId={OrgId}", workspaceId, orgId);
        try
        {
            var workspace = await _workspaceRepository.GetByIdForOrgAsync(workspaceId, orgId, cancellationToken);
            _logger.LogDebug(
                "WorkspaceService.GetByIdAsync exit workspaceId={WorkspaceId} found={Found}", workspaceId, workspace is not null);
            return workspace;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.GetByIdAsync error workspaceId={WorkspaceId} orgId={OrgId}", workspaceId, orgId);
            throw;
        }
    }

    public async Task<List<Core.Entities.Workspaces.Workspace>> GetForOrgAsync(
        Guid orgId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceService.GetForOrgAsync enter orgId={OrgId}", orgId);
        try
        {
            var workspaces = await _workspaceRepository.GetForOrgAsync(orgId, cancellationToken);
            _logger.LogDebug("WorkspaceService.GetForOrgAsync exit orgId={OrgId} count={Count}", orgId, workspaces.Count);
            return workspaces;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.GetForOrgAsync error orgId={OrgId}", orgId);
            throw;
        }
    }

    public async Task<List<WorkspaceEnvironment>> GetEnvironmentsAsync(
        Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceService.GetEnvironmentsAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var environments = await _workspaceRepository.GetEnvironmentsAsync(workspaceId, cancellationToken);
            var ordered = environments.OrderBy(e => e.Name).ToList();
            _logger.LogDebug(
                "WorkspaceService.GetEnvironmentsAsync exit workspaceId={WorkspaceId} count={Count}", workspaceId, ordered.Count);
            return ordered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.GetEnvironmentsAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task UpdateSettingsAsync(
        Guid workspaceId, WorkspaceSettings settings, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceService.UpdateSettingsAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken)
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

            workspace.Settings = settings;
            _workspaceRepository.Update(workspace);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("WorkspaceService.UpdateSettingsAsync exit workspaceId={WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.UpdateSettingsAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task SoftDeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceService.SoftDeleteAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
            if (workspace is null)
            {
                _logger.LogWarning("WorkspaceService.SoftDeleteAsync no-op workspaceId={WorkspaceId} — not found", workspaceId);
                return;
            }

            workspace.IsDeleted = true;
            workspace.DeletedAt = DateTime.UtcNow;
            _workspaceRepository.Update(workspace);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("WorkspaceService.SoftDeleteAsync exit workspaceId={WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceService.SoftDeleteAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }
}
