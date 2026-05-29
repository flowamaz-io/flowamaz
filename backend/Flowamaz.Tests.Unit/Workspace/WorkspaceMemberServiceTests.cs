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

public class WorkspaceMemberServiceTests
{
    private readonly Mock<IWorkspaceMemberRepository> _memberRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<Flowamaz.Core.Interfaces.Services.IEditionService> _edition = new();
    private readonly Guid _workspaceId = Guid.NewGuid();

    private WorkspaceMemberService CreateService()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new WorkspaceMemberService(_memberRepo.Object, _uow.Object, _edition.Object, NullLogger<WorkspaceMemberService>.Instance);
    }

    [Fact]
    public async Task AddMemberAsync_adds_active_member_with_requested_role()
    {
        WorkspaceMember? captured = null;
        var orgUserId = Guid.NewGuid();
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, orgUserId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceMember?)null);
        _memberRepo.Setup(r => r.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMember, CancellationToken>((m, _) => captured = m).Returns(Task.CompletedTask);

        var service = CreateService();

        await service.AddMemberAsync(_workspaceId, orgUserId, WorkspaceRole.Designer, Guid.NewGuid());

        captured!.Role.Should().Be(WorkspaceRole.Designer);
        captured.IsActive.Should().BeTrue();
        captured.WorkspaceId.Should().Be(_workspaceId);
    }

    [Fact]
    public async Task UpdateRoleAsync_self_role_change_throws_at_service_layer()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        var act = () => service.UpdateRoleAsync(_workspaceId, userId, WorkspaceRole.Viewer, userId);

        await act.Should().ThrowAsync<SelfModificationException>();
        _memberRepo.Verify(r => r.Update(It.IsAny<WorkspaceMember>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRoleAsync_demoting_the_last_admin_throws_LastAdminException()
    {
        var target = Guid.NewGuid();
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = target, Role = WorkspaceRole.Admin, IsActive = true });
        _memberRepo.Setup(r => r.CountActiveAdminsAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = CreateService();

        var act = () => service.UpdateRoleAsync(_workspaceId, target, WorkspaceRole.Designer, Guid.NewGuid());

        await act.Should().ThrowAsync<LastAdminException>();
    }

    [Fact]
    public async Task UpdateRoleAsync_demoting_an_admin_when_others_exist_succeeds()
    {
        var target = Guid.NewGuid();
        var member = new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = target, Role = WorkspaceRole.Admin, IsActive = true };
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, target, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        _memberRepo.Setup(r => r.CountActiveAdminsAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var service = CreateService();

        await service.UpdateRoleAsync(_workspaceId, target, WorkspaceRole.Operator, Guid.NewGuid());

        member.Role.Should().Be(WorkspaceRole.Operator);
        _memberRepo.Verify(r => r.Update(member), Times.Once);
    }

    [Fact]
    public async Task RemoveMemberAsync_self_removal_throws_at_service_layer()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        var act = () => service.RemoveMemberAsync(_workspaceId, userId, userId);

        await act.Should().ThrowAsync<SelfModificationException>();
        _memberRepo.Verify(r => r.Remove(It.IsAny<WorkspaceMember>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMemberAsync_removing_the_last_admin_throws_LastAdminException()
    {
        var target = Guid.NewGuid();
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = target, Role = WorkspaceRole.Admin, IsActive = true });
        _memberRepo.Setup(r => r.CountActiveAdminsAsync(_workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = CreateService();

        var act = () => service.RemoveMemberAsync(_workspaceId, target, Guid.NewGuid());

        await act.Should().ThrowAsync<LastAdminException>();
        _memberRepo.Verify(r => r.Remove(It.IsAny<WorkspaceMember>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMemberAsync_removes_a_non_admin_member()
    {
        var target = Guid.NewGuid();
        var member = new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = target, Role = WorkspaceRole.Operator, IsActive = true };
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, target, It.IsAny<CancellationToken>())).ReturnsAsync(member);

        var service = CreateService();

        await service.RemoveMemberAsync(_workspaceId, target, Guid.NewGuid());

        _memberRepo.Verify(r => r.Remove(member), Times.Once);
    }

    [Fact]
    public async Task GetMemberRoleAsync_returns_null_for_inactive_member()
    {
        var orgUserId = Guid.NewGuid();
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, orgUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = orgUserId, Role = WorkspaceRole.Admin, IsActive = false });

        var service = CreateService();

        var role = await service.GetMemberRoleAsync(_workspaceId, orgUserId);

        role.Should().BeNull();
    }
}
