using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workspaces;

public class WorkspaceApiKeyServiceTests
{
    private readonly Mock<IWorkspaceApiKeyRepository> _apiKeyRepo = new();
    private readonly Mock<IWorkspaceRepository> _workspaceRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _environmentId = Guid.NewGuid();

    private WorkspaceApiKeyService CreateService(WorkspaceEnvironmentType envType = WorkspaceEnvironmentType.Production)
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _workspaceRepo.Setup(r => r.GetByIdAsync(_workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = _workspaceId, Slug = "finops", Name = "Fin Ops" });
        _workspaceRepo.Setup(r => r.GetEnvironmentByIdAsync(_environmentId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceEnvironment { Id = _environmentId, WorkspaceId = _workspaceId, Name = envType });
        return new WorkspaceApiKeyService(
            _apiKeyRepo.Object, _workspaceRepo.Object, _uow.Object, NullLogger<WorkspaceApiKeyService>.Instance);
    }

    [Fact]
    public async Task CreateApiKeyAsync_then_ValidateApiKeyAsync_round_trips_via_sha256_hash()
    {
        WorkspaceApiKey? stored = null;
        _apiKeyRepo.Setup(r => r.AddAsync(It.IsAny<WorkspaceApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceApiKey, CancellationToken>((k, _) => stored = k).Returns(Task.CompletedTask);
        // The store returns the persisted key only for its actual hash — proving the round-trip hashes the plain key.
        _apiKeyRepo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string hash, CancellationToken _) => stored is not null && stored.KeyHash == hash ? stored : null);

        var service = CreateService();

        var created = await service.CreateApiKeyAsync(
            _workspaceId, _environmentId, "CI key", [ApiKeyScope.WorkflowsTrigger, ApiKeyScope.InstancesRead], Guid.NewGuid());

        created.PlainKey.Should().StartWith("fmz_live_finops_");
        stored!.KeyHash.Should().NotBe(created.PlainKey);
        stored.KeyHash.Should().HaveLength(64); // sha256 hex
        stored.KeyPrefix.Should().Be(created.PlainKey[..16]);

        var validation = await service.ValidateApiKeyAsync(created.PlainKey);

        validation.Should().NotBeNull();
        validation!.WorkspaceId.Should().Be(_workspaceId);
        validation.EnvironmentId.Should().Be(_environmentId);
        validation.KeyId.Should().Be(stored.Id);
        validation.Scopes.Should().BeEquivalentTo(new[] { ApiKeyScope.WorkflowsTrigger, ApiKeyScope.InstancesRead });
    }

    [Fact]
    public async Task CreateApiKeyAsync_uses_test_prefix_for_non_production_environments()
    {
        _apiKeyRepo.Setup(r => r.AddAsync(It.IsAny<WorkspaceApiKey>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = CreateService(WorkspaceEnvironmentType.Dev);

        var created = await service.CreateApiKeyAsync(
            _workspaceId, _environmentId, "Dev key", [ApiKeyScope.WorkflowsRead], Guid.NewGuid());

        created.PlainKey.Should().StartWith("fmz_test_finops_");
    }

    [Fact]
    public async Task CreateApiKeyAsync_rejects_unknown_scopes()
    {
        var service = CreateService();

        var act = () => service.CreateApiKeyAsync(
            _workspaceId, _environmentId, "Bad key", ["workflows:read", "totally:bogus"], Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*totally:bogus*");
        _apiKeyRepo.Verify(r => r.AddAsync(It.IsAny<WorkspaceApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateApiKeyAsync_returns_null_for_revoked_key()
    {
        var service = CreateService();
        _apiKeyRepo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceApiKey { WorkspaceId = _workspaceId, IsActive = false });

        var validation = await service.ValidateApiKeyAsync("fmz_live_finops_deadbeef");

        validation.Should().BeNull();
    }

    [Fact]
    public async Task ValidateApiKeyAsync_returns_null_for_expired_key()
    {
        var service = CreateService();
        _apiKeyRepo.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceApiKey { WorkspaceId = _workspaceId, IsActive = true, ExpiresAt = DateTime.UtcNow.AddMinutes(-1) });

        var validation = await service.ValidateApiKeyAsync("fmz_live_finops_deadbeef");

        validation.Should().BeNull();
    }

    [Fact]
    public async Task ValidateApiKeyAsync_returns_null_for_empty_input()
    {
        var service = CreateService();

        (await service.ValidateApiKeyAsync("")).Should().BeNull();
        _apiKeyRepo.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_deactivates_the_key_within_its_workspace()
    {
        var keyId = Guid.NewGuid();
        var key = new WorkspaceApiKey { Id = keyId, WorkspaceId = _workspaceId, IsActive = true };
        _apiKeyRepo.Setup(r => r.GetByIdAsync(keyId, _workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(key);
        var service = CreateService();

        await service.RevokeApiKeyAsync(keyId, _workspaceId);

        key.IsActive.Should().BeFalse();
        _apiKeyRepo.Verify(r => r.Update(key), Times.Once);
    }
}
