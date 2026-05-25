using Flowamaz.Application.Auth.DTOs;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Application.Auth.Validators;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Auth;

public class AuthServiceTests
{
    private readonly Mock<IOrganisationService> _orgService = new();
    private readonly Mock<IOrgUserService> _orgUserService = new();
    private readonly Mock<IOrgUserRepository> _orgUserRepo = new();
    private readonly Mock<IWorkspaceMemberRepository> _memberRepo = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();

    private const string GoodPassword = "Sup3rSecret!";

    private AuthService CreateService()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<OrgUser>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceMembership>>()))
            .Returns("access-token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-refresh", "hash-refresh"));
        _jwt.Setup(j => j.HashRefreshToken(It.IsAny<string>())).Returns((string p) => $"{p}-hash");
        _memberRepo.Setup(r => r.GetActiveMembershipsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new AuthService(
            new RegisterRequestValidator(), new LoginRequestValidator(),
            _orgService.Object, _orgUserService.Object, _orgUserRepo.Object, _memberRepo.Object,
            _refreshRepo.Object, _jwt.Object, _uow.Object,
            Options.Create(new JwtOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 7 }),
            NullLogger<AuthService>.Instance);
    }

    private static OrgUser UserWithPassword(string password) => new()
    {
        Id = Guid.NewGuid(),
        OrgId = Guid.NewGuid(),
        Email = "ada@acme.test",
        Name = "Ada",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
        IsActive = true,
    };

    private LoginRequest Login(string password = GoodPassword) => new("ada@acme.test", password, "acme");

    // ── Register ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_issues_tokens_for_the_new_owner()
    {
        var ownerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _orgService.Setup(s => s.RegisterOrganisationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Core.Enums.DataRegion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgRegistrationResult(orgId, "acme", ownerId, "ada@acme.test", "starter", Core.Enums.OrgStatus.Trial, DateTime.UtcNow.AddDays(14)));
        _orgUserRepo.Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgUser { Id = ownerId, OrgId = orgId, Email = "ada@acme.test", Name = "Ada" });

        var service = CreateService();
        var request = new RegisterRequest("Acme", "acme", "billing@acme.test", "ada@acme.test", "Ada", GoodPassword, "starter");

        var result = await service.RegisterAsync(request, "1.2.3.4");

        result.AccessToken.Should().Be("access-token");
        result.RefreshTokenPlain.Should().Be("plain-refresh");
        result.User.OrgSlug.Should().Be("acme");
        _refreshRepo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t => t.TokenHash == "hash-refresh"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_rejects_weak_password_before_touching_persistence()
    {
        var service = CreateService();
        var request = new RegisterRequest("Acme", "acme", "billing@acme.test", "ada@acme.test", "Ada", "weak", "starter");

        await service.Invoking(s => s.RegisterAsync(request, "1.2.3.4"))
            .Should().ThrowAsync<FluentValidation.ValidationException>();
        _orgService.Verify(s => s.RegisterOrganisationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Core.Enums.DataRegion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Login: no user enumeration ─────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_unknown_org_and_wrong_password_throw_the_same_exception()
    {
        // Unknown org
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync((Organisation?)null);
        var unknownOrg = CreateService();
        await unknownOrg.Invoking(s => s.LoginAsync(Login(), "ip")).Should().ThrowAsync<InvalidCredentialsException>();

        // Known org, wrong password
        var user = UserWithPassword(GoodPassword);
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        _orgUserService.Setup(s => s.GetByEmailAndOrgAsync("ada@acme.test", user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var wrongPw = CreateService();
        await wrongPw.Invoking(s => s.LoginAsync(Login("WrongPass1"), "ip")).Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task LoginAsync_wrong_password_records_failed_attempt()
    {
        var user = UserWithPassword(GoodPassword);
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        _orgUserService.Setup(s => s.GetByEmailAndOrgAsync(user.Email, user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var service = CreateService();
        await service.Invoking(s => s.LoginAsync(Login("WrongPass1"), "ip")).Should().ThrowAsync<InvalidCredentialsException>();

        _orgUserService.Verify(s => s.RecordFailedLoginAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _orgUserService.Verify(s => s.LockAccountAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_locks_account_on_fifth_failed_attempt()
    {
        var user = UserWithPassword(GoodPassword);
        user.FailedLoginCount = 4; // this attempt makes it 5
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        _orgUserService.Setup(s => s.GetByEmailAndOrgAsync(user.Email, user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _orgUserService.Setup(s => s.RecordFailedLoginAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => user.FailedLoginCount++).Returns(Task.CompletedTask);

        var service = CreateService();
        await service.Invoking(s => s.LoginAsync(Login("WrongPass1"), "ip")).Should().ThrowAsync<InvalidCredentialsException>();

        _orgUserService.Verify(s => s.LockAccountAsync(user.Id, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_throws_locked_when_account_is_locked()
    {
        var user = UserWithPassword(GoodPassword);
        user.LockoutUntil = DateTime.UtcNow.AddMinutes(10);
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        _orgUserService.Setup(s => s.GetByEmailAndOrgAsync(user.Email, user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var service = CreateService();
        await service.Invoking(s => s.LoginAsync(Login(), "ip")).Should().ThrowAsync<AccountLockedException>();
    }

    [Fact]
    public async Task LoginAsync_success_resets_failures_and_issues_tokens()
    {
        var user = UserWithPassword(GoodPassword);
        _orgService.Setup(s => s.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        _orgUserService.Setup(s => s.GetByEmailAndOrgAsync(user.Email, user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var service = CreateService();
        var result = await service.LoginAsync(Login(), "ip");

        result.AccessToken.Should().Be("access-token");
        _orgUserService.Verify(s => s.ResetFailedLoginCountAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _orgUserService.Verify(s => s.UpdateLastLoginAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Refresh rotation ───────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_rotates_revoking_old_token_and_issuing_a_new_one()
    {
        var user = UserWithPassword(GoodPassword);
        var oldToken = new RefreshToken { OrgUserId = user.Id, TokenHash = "oldplain-hash", ExpiresAt = DateTime.UtcNow.AddDays(3) };
        _refreshRepo.Setup(r => r.GetByHashAsync("oldplain-hash", It.IsAny<CancellationToken>())).ReturnsAsync(oldToken);
        _orgUserRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _orgService.Setup(s => s.GetByIdAsync(user.OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });

        var service = CreateService();
        var result = await service.RefreshAsync("oldplain", "ip");

        result.Should().NotBeNull();
        oldToken.RevokedAt.Should().NotBeNull("the presented token must be revoked on rotation");
        _refreshRepo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t => t.TokenHash == "hash-refresh"), It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_returns_null_for_already_revoked_token()
    {
        var revoked = new RefreshToken { TokenHash = "x-hash", ExpiresAt = DateTime.UtcNow.AddDays(1), RevokedAt = DateTime.UtcNow.AddMinutes(-1) };
        _refreshRepo.Setup(r => r.GetByHashAsync("x-hash", It.IsAny<CancellationToken>())).ReturnsAsync(revoked);

        var service = CreateService();
        (await service.RefreshAsync("x", "ip")).Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_returns_null_when_no_cookie()
    {
        var service = CreateService();
        (await service.RefreshAsync(null, "ip")).Should().BeNull();
    }
}
