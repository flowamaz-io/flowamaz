using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workspaces;

public class WorkspaceAuthorizationServiceTests
{
    private readonly Mock<IWorkspaceMemberRepository> _memberRepo = new();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private WorkspaceAuthorizationService CreateService() =>
        new(_memberRepo.Object, NullLogger<WorkspaceAuthorizationService>.Instance);

    private void SetRole(WorkspaceRole role, bool isActive = true) =>
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceMember { WorkspaceId = _workspaceId, OrgUserId = _userId, Role = role, IsActive = isActive });

    [Fact]
    public async Task RequireMinimumRoleAsync_viewer_cannot_pass_operator_requirement()
    {
        SetRole(WorkspaceRole.Viewer);
        var service = CreateService();

        var act = () => service.RequireMinimumRoleAsync(_workspaceId, _userId, WorkspaceRole.Operator);

        var ex = (await act.Should().ThrowAsync<InsufficientRoleException>()).Which;
        ex.RequiredRole.Should().Be(WorkspaceRole.Operator);
        ex.ActualRole.Should().Be(WorkspaceRole.Viewer);
    }

    [Fact]
    public async Task RequireMinimumRoleAsync_operator_passes_operator_requirement()
    {
        SetRole(WorkspaceRole.Operator);
        var service = CreateService();

        var act = () => service.RequireMinimumRoleAsync(_workspaceId, _userId, WorkspaceRole.Operator);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequireMinimumRoleAsync_admin_passes_lower_requirement()
    {
        SetRole(WorkspaceRole.Admin);
        var service = CreateService();

        await service.Invoking(s => s.RequireMinimumRoleAsync(_workspaceId, _userId, WorkspaceRole.Designer))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequireMinimumRoleAsync_non_member_throws_with_null_actual_role()
    {
        _memberRepo.Setup(r => r.GetAsync(_workspaceId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceMember?)null);
        var service = CreateService();

        var ex = (await service.Invoking(s => s.RequireMinimumRoleAsync(_workspaceId, _userId, WorkspaceRole.Viewer))
            .Should().ThrowAsync<InsufficientRoleException>()).Which;
        ex.ActualRole.Should().BeNull();
    }

    [Fact]
    public async Task RequireMinimumRoleAsync_inactive_member_has_no_role()
    {
        SetRole(WorkspaceRole.Admin, isActive: false);
        var service = CreateService();

        await service.Invoking(s => s.RequireMinimumRoleAsync(_workspaceId, _userId, WorkspaceRole.Viewer))
            .Should().ThrowAsync<InsufficientRoleException>();
    }

    [Theory]
    [InlineData("workspace.settings", WorkspaceRole.Designer, false)]
    [InlineData("workspace.settings", WorkspaceRole.Admin, true)]
    [InlineData("instances.trigger", WorkspaceRole.Operator, true)]
    [InlineData("instances.trigger", WorkspaceRole.Runner, false)]
    [InlineData("workflows.read", WorkspaceRole.Viewer, true)]
    public async Task HasPermissionAsync_maps_permission_to_minimum_role(string permission, WorkspaceRole role, bool expected)
    {
        SetRole(role);
        var service = CreateService();

        var granted = await service.HasPermissionAsync(_workspaceId, _userId, permission);

        granted.Should().Be(expected);
    }

    [Fact]
    public async Task HasPermissionAsync_unknown_permission_is_denied_by_default()
    {
        SetRole(WorkspaceRole.Admin);
        var service = CreateService();

        var granted = await service.HasPermissionAsync(_workspaceId, _userId, "totally.unknown.permission");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task IsWorkspaceAdminAsync_true_only_for_admin()
    {
        SetRole(WorkspaceRole.Designer);
        var service = CreateService();
        (await service.IsWorkspaceAdminAsync(_workspaceId, _userId)).Should().BeFalse();

        SetRole(WorkspaceRole.Admin);
        (await service.IsWorkspaceAdminAsync(_workspaceId, _userId)).Should().BeTrue();
    }

    [Fact]
    public void ValidateApiKeyScope_checks_membership_of_the_granted_scopes()
    {
        var service = CreateService();
        var scopes = new[] { ApiKeyScope.WorkflowsRead, ApiKeyScope.InstancesRead };

        service.ValidateApiKeyScope(scopes, ApiKeyScope.WorkflowsRead).Should().BeTrue();
        service.ValidateApiKeyScope(scopes, ApiKeyScope.InstancesWrite).Should().BeFalse();
    }
}
