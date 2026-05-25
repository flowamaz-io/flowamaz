using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workspaces;

/// <summary>
/// Covers the read/getter and mutation paths of the workspace services that the happy-path tests
/// don't reach (list/get/update/delete, member reads, api-key reads + last-used bookkeeping).
/// </summary>
public class WorkspaceServicesCoverageTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    public WorkspaceServicesCoverageTests() =>
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

    // ── WorkspaceService ────────────────────────────────────────────────────────

    private (WorkspaceService svc, Mock<IWorkspaceRepository> repo) NewWorkspaceService()
    {
        var repo = new Mock<IWorkspaceRepository>();
        var members = new Mock<IWorkspaceMemberRepository>();
        return (new WorkspaceService(repo.Object, members.Object, _uow.Object, NullLogger<WorkspaceService>.Instance), repo);
    }

    [Fact]
    public async Task WorkspaceService_GetForOrg_returns_repo_list()
    {
        var (svc, repo) = NewWorkspaceService();
        repo.Setup(r => r.GetForOrgAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Workspace { OrgId = _orgId, Slug = "a" }, new Workspace { OrgId = _orgId, Slug = "b" }]);

        (await svc.GetForOrgAsync(_orgId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task WorkspaceService_GetById_returns_org_scoped_workspace()
    {
        var (svc, repo) = NewWorkspaceService();
        repo.Setup(r => r.GetByIdForOrgAsync(_workspaceId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = _workspaceId, OrgId = _orgId, Slug = "ws" });

        (await svc.GetByIdAsync(_workspaceId, _orgId))!.Slug.Should().Be("ws");
    }

    [Fact]
    public async Task WorkspaceService_UpdateSettings_persists_new_settings()
    {
        var (svc, repo) = NewWorkspaceService();
        var workspace = new Workspace { Id = _workspaceId, OrgId = _orgId };
        repo.Setup(r => r.GetByIdAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(workspace);

        var settings = new WorkspaceSettings { MaxConcurrentRuns = 42, RunRetentionDays = 90 };
        await svc.UpdateSettingsAsync(_workspaceId, settings);

        workspace.Settings.MaxConcurrentRuns.Should().Be(42);
        repo.Verify(r => r.Update(workspace), Times.Once);
    }

    [Fact]
    public async Task WorkspaceService_SoftDelete_marks_deleted()
    {
        var (svc, repo) = NewWorkspaceService();
        var workspace = new Workspace { Id = _workspaceId };
        repo.Setup(r => r.GetByIdAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(workspace);

        await svc.SoftDeleteAsync(_workspaceId);

        workspace.IsDeleted.Should().BeTrue();
        workspace.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task WorkspaceService_SoftDelete_missing_workspace_is_noop()
    {
        var (svc, repo) = NewWorkspaceService();
        repo.Setup(r => r.GetByIdAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync((Workspace?)null);

        await svc.Invoking(s => s.SoftDeleteAsync(_workspaceId)).Should().NotThrowAsync();
        repo.Verify(r => r.Update(It.IsAny<Workspace>()), Times.Never);
    }

    // ── WorkspaceMemberService ───────────────────────────────────────────────────

    private (WorkspaceMemberService svc, Mock<IWorkspaceMemberRepository> repo) NewMemberService()
    {
        var repo = new Mock<IWorkspaceMemberRepository>();
        return (new WorkspaceMemberService(repo.Object, _uow.Object, NullLogger<WorkspaceMemberService>.Instance), repo);
    }

    [Fact]
    public async Task MemberService_GetMembers_maps_dtos()
    {
        var (svc, repo) = NewMemberService();
        repo.Setup(r => r.GetForWorkspaceAsync(_workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Operator, IsActive = true }]);

        var dtos = await svc.GetMembersAsync(_workspaceId);
        dtos.Should().ContainSingle().Which.Role.Should().Be(WorkspaceRole.Operator);
    }

    [Fact]
    public async Task MemberService_GetDetailedMembers_returns_repo_rows()
    {
        var (svc, repo) = NewMemberService();
        repo.Setup(r => r.GetDetailedMembersAsync(_workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkspaceMemberDetailDto(Guid.NewGuid(), "a@b.test", "A", WorkspaceRole.Admin, DateTime.UtcNow, true)]);

        (await svc.GetDetailedMembersAsync(_workspaceId)).Should().ContainSingle().Which.Email.Should().Be("a@b.test");
    }

    [Fact]
    public async Task MemberService_IsMember_true_for_active_member()
    {
        var (svc, repo) = NewMemberService();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(_workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { OrgUserId = userId, IsActive = true });

        (await svc.IsMemberAsync(_workspaceId, userId)).Should().BeTrue();
    }

    [Fact]
    public async Task MemberService_GetMemberRole_returns_role_for_active_member()
    {
        var (svc, repo) = NewMemberService();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(_workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { OrgUserId = userId, Role = WorkspaceRole.Designer, IsActive = true });

        (await svc.GetMemberRoleAsync(_workspaceId, userId)).Should().Be(WorkspaceRole.Designer);
    }

    [Fact]
    public async Task MemberService_AddMember_existing_throws()
    {
        var (svc, repo) = NewMemberService();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(_workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { OrgUserId = userId, IsActive = true });

        await svc.Invoking(s => s.AddMemberAsync(_workspaceId, userId, WorkspaceRole.Viewer, Guid.NewGuid()))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MemberService_RemoveMember_missing_is_noop()
    {
        var (svc, repo) = NewMemberService();
        var target = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(_workspaceId, target, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceMember?)null);

        await svc.RemoveMemberAsync(_workspaceId, target, Guid.NewGuid());
        repo.Verify(r => r.Remove(It.IsAny<WorkspaceMember>()), Times.Never);
    }

    // ── WorkspaceApiKeyService ───────────────────────────────────────────────────

    private (WorkspaceApiKeyService svc, Mock<IWorkspaceApiKeyRepository> keys) NewApiKeyService()
    {
        var keys = new Mock<IWorkspaceApiKeyRepository>();
        var workspaces = new Mock<IWorkspaceRepository>();
        return (new WorkspaceApiKeyService(keys.Object, workspaces.Object, _uow.Object, NullLogger<WorkspaceApiKeyService>.Instance), keys);
    }

    [Fact]
    public async Task ApiKeyService_GetApiKeys_maps_dtos_without_plain_key()
    {
        var (svc, keys) = NewApiKeyService();
        keys.Setup(r => r.GetForWorkspaceAsync(_workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkspaceApiKey { WorkspaceId = _workspaceId, Name = "k", KeyPrefix = "fmz_live_x", Scopes = ["workflows:read"], IsActive = true }]);

        var dtos = await svc.GetApiKeysAsync(_workspaceId);
        dtos.Should().ContainSingle().Which.KeyPrefix.Should().Be("fmz_live_x");
    }

    [Fact]
    public async Task ApiKeyService_RecordLastUsed_sets_timestamp()
    {
        var (svc, keys) = NewApiKeyService();
        var keyId = Guid.NewGuid();
        var key = new WorkspaceApiKey { Id = keyId, WorkspaceId = _workspaceId };
        keys.Setup(r => r.FindByIdAsync(keyId, It.IsAny<CancellationToken>())).ReturnsAsync(key);

        await svc.RecordLastUsedAsync(keyId);

        key.LastUsedAt.Should().NotBeNull();
        keys.Verify(r => r.Update(key), Times.Once);
    }

    [Fact]
    public async Task ApiKeyService_RecordLastUsed_missing_is_noop()
    {
        var (svc, keys) = NewApiKeyService();
        var keyId = Guid.NewGuid();
        keys.Setup(r => r.FindByIdAsync(keyId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceApiKey?)null);

        await svc.RecordLastUsedAsync(keyId);
        keys.Verify(r => r.Update(It.IsAny<WorkspaceApiKey>()), Times.Never);
    }

    [Fact]
    public async Task ApiKeyService_Revoke_missing_is_noop()
    {
        var (svc, keys) = NewApiKeyService();
        var keyId = Guid.NewGuid();
        keys.Setup(r => r.GetByIdAsync(keyId, _workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceApiKey?)null);

        await svc.RevokeApiKeyAsync(keyId, _workspaceId);
        keys.Verify(r => r.Update(It.IsAny<WorkspaceApiKey>()), Times.Never);
    }
}
