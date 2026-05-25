using Flowamaz.Application.Platform.Services;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Platform;

public class OrgUserServiceTests
{
    private const string Password = "Sup3rSecret!";
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Mock<IOrgUserRepository> _orgUserRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private OrgUserService CreateService() =>
        new(_orgUserRepo.Object, _uow.Object, NullLogger<OrgUserService>.Instance);

    private OrgUser ActiveUser() => new()
    {
        Id = Guid.NewGuid(),
        OrgId = _orgId,
        Email = "user@acme.test",
        Name = "User",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 12),
        IsActive = true,
    };

    [Fact]
    public async Task ValidateCredentialsAsync_correct_password_returns_user()
    {
        var user = ActiveUser();
        _orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(user.Email, _orgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateService().ValidateCredentialsAsync(user.Email, _orgId, Password);

        result.Should().BeSameAs(user);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_wrong_password_returns_null()
    {
        var user = ActiveUser();
        _orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(user.Email, _orgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateService().ValidateCredentialsAsync(user.Email, _orgId, "wrong-password");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_inactive_user_returns_null()
    {
        var user = ActiveUser();
        user.IsActive = false;
        _orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(user.Email, _orgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateService().ValidateCredentialsAsync(user.Email, _orgId, Password);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_locked_out_user_returns_null()
    {
        var user = ActiveUser();
        user.LockoutUntil = DateTime.UtcNow.AddMinutes(10);
        _orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(user.Email, _orgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateService().ValidateCredentialsAsync(user.Email, _orgId, Password);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_unknown_user_returns_null()
    {
        _orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(It.IsAny<string>(), _orgId, It.IsAny<CancellationToken>())).ReturnsAsync((OrgUser?)null);

        var result = await CreateService().ValidateCredentialsAsync("ghost@acme.test", _orgId, Password);

        result.Should().BeNull();
    }

    [Fact]
    public async Task IsLockedOutAsync_reflects_lockout_window()
    {
        var locked = ActiveUser();
        locked.LockoutUntil = DateTime.UtcNow.AddMinutes(5);
        _orgUserRepo.Setup(r => r.GetByIdAsync(locked.Id, It.IsAny<CancellationToken>())).ReturnsAsync(locked);

        (await CreateService().IsLockedOutAsync(locked.Id)).Should().BeTrue();

        var expired = ActiveUser();
        expired.LockoutUntil = DateTime.UtcNow.AddMinutes(-5);
        _orgUserRepo.Setup(r => r.GetByIdAsync(expired.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expired);

        (await CreateService().IsLockedOutAsync(expired.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RecordFailedLoginAsync_increments_count_and_saves()
    {
        var user = ActiveUser();
        user.FailedLoginCount = 2;
        _orgUserRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await CreateService().RecordFailedLoginAsync(user.Id);

        user.FailedLoginCount.Should().Be(3);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LockAccountAsync_sets_lockout_and_ResetFailedLoginCount_clears_state()
    {
        var user = ActiveUser();
        user.FailedLoginCount = 5;
        _orgUserRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var service = CreateService();

        await service.LockAccountAsync(user.Id, TimeSpan.FromMinutes(15));
        user.LockoutUntil.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        await service.ResetFailedLoginCountAsync(user.Id);
        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateLastLoginAsync_sets_timestamp_and_saves()
    {
        var user = ActiveUser();
        _orgUserRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await CreateService().UpdateLastLoginAsync(user.Id);

        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
