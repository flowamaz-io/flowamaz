using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workspaces;

public class WorkspaceServiceTests
{
    private readonly Mock<IWorkspaceRepository> _workspaceRepo = new();
    private readonly Mock<IWorkspaceMemberRepository> _memberRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();

    private WorkspaceService CreateService()
    {
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new WorkspaceService(
            _workspaceRepo.Object, _memberRepo.Object, _uow.Object, NullLogger<WorkspaceService>.Instance);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_creates_workspace_three_environments_and_admin_member_atomically()
    {
        Workspace? capturedWorkspace = null;
        var capturedEnvironments = new List<WorkspaceEnvironment>();
        WorkspaceMember? capturedMember = null;
        var orgId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        _workspaceRepo.Setup(r => r.SlugExistsInOrgAsync(orgId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _workspaceRepo.Setup(r => r.AddAsync(It.IsAny<Workspace>(), It.IsAny<CancellationToken>()))
            .Callback<Workspace, CancellationToken>((w, _) => capturedWorkspace = w).Returns(Task.CompletedTask);
        _workspaceRepo.Setup(r => r.AddEnvironmentAsync(It.IsAny<WorkspaceEnvironment>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceEnvironment, CancellationToken>((e, _) => capturedEnvironments.Add(e)).Returns(Task.CompletedTask);
        _memberRepo.Setup(r => r.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMember, CancellationToken>((m, _) => capturedMember = m).Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.CreateWorkspaceAsync(orgId, "Finance Ops", "Finance-Ops", creatorId);

        result.OrgId.Should().Be(orgId);
        result.Slug.Should().Be("finance-ops");

        capturedEnvironments.Should().HaveCount(3);
        capturedEnvironments.Select(e => e.Name).Should().BeEquivalentTo(
            new[] { WorkspaceEnvironmentType.Dev, WorkspaceEnvironmentType.Staging, WorkspaceEnvironmentType.Production });
        capturedEnvironments.Should().OnlyContain(e => e.WorkspaceId == capturedWorkspace!.Id);

        capturedMember!.OrgUserId.Should().Be(creatorId);
        capturedMember.Role.Should().Be(WorkspaceRole.Admin);
        capturedMember.IsActive.Should().BeTrue();
        capturedMember.WorkspaceId.Should().Be(capturedWorkspace!.Id);

        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_duplicate_slug_in_org_throws_and_never_opens_transaction()
    {
        var orgId = Guid.NewGuid();
        _workspaceRepo.Setup(r => r.SlugExistsInOrgAsync(orgId, "taken", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = CreateService();

        var act = () => service.CreateWorkspaceAsync(orgId, "Taken", "taken", Guid.NewGuid());

        await act.Should().ThrowAsync<SlugAlreadyExistsException>();
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_rolls_back_when_persistence_fails()
    {
        var orgId = Guid.NewGuid();
        _workspaceRepo.Setup(r => r.SlugExistsInOrgAsync(orgId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var service = new WorkspaceService(
            _workspaceRepo.Object, _memberRepo.Object, _uow.Object, NullLogger<WorkspaceService>.Instance);

        var act = () => service.CreateWorkspaceAsync(orgId, "Acme", "acme", Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_workspace_belongs_to_a_different_org()
    {
        var workspaceId = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        // Repository enforces the org boundary: wrong org → null.
        _workspaceRepo.Setup(r => r.GetByIdForOrgAsync(workspaceId, otherOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);

        var service = CreateService();

        var result = await service.GetByIdAsync(workspaceId, otherOrg);

        result.Should().BeNull();
    }
}
