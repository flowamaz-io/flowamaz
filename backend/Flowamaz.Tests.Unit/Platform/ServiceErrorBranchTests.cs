using Flowamaz.Application.Auth.DTOs;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Application.Auth.Validators;
using Flowamaz.Application.Platform.Services;
using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Platform;

/// <summary>
/// Exercises the error-handling catch branches of the transactional/IO services: a persistence
/// fault must roll back the transaction (where one is open) and re-propagate, never be swallowed.
/// </summary>
public class ServiceErrorBranchTests
{
    private static readonly InvalidOperationException DbFault = new("simulated persistence failure");

    private static Mock<IUnitOfWork> UowThatFailsOnSave(Mock<IUnitOfWorkTransaction> tx)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(DbFault);
        return uow;
    }

    // ── OrganisationService.RegisterOrganisationAsync — transaction failure ────────

    [Fact]
    public async Task OrganisationService_register_rolls_back_and_rethrows_on_save_failure()
    {
        var tx = new Mock<IUnitOfWorkTransaction>();
        var uow = UowThatFailsOnSave(tx);
        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetBySlugAsync("starter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Plan { Id = Guid.NewGuid(), Slug = "starter" });
        var orgRepo = new Mock<IOrganisationRepository>();
        orgRepo.Setup(r => r.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var orgUserRepo = new Mock<IOrgUserRepository>();
        var email = new Mock<IEmailService>();

        var service = new OrganisationService(
            planRepo.Object, orgRepo.Object, orgUserRepo.Object, uow.Object, email.Object,
            NullLogger<OrganisationService>.Instance);

        await service.Invoking(s => s.RegisterOrganisationAsync(
                "Acme", "acme", "billing@acme.test", "ada@acme.test", "Ada", "Sup3rSecret!",
                "starter", DataRegion.EuWest1))
            .Should().ThrowAsync<InvalidOperationException>();

        tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── OrgUserService.ValidateCredentialsAsync — DB error propagates ──────────────

    [Fact]
    public async Task OrgUserService_validate_credentials_rethrows_on_repository_error()
    {
        var orgUserRepo = new Mock<IOrgUserRepository>();
        orgUserRepo.Setup(r => r.GetByEmailAndOrgAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(DbFault);
        var service = new OrgUserService(orgUserRepo.Object, Mock.Of<IUnitOfWork>(), NullLogger<OrgUserService>.Instance);

        await service.Invoking(s => s.ValidateCredentialsAsync("ada@acme.test", Guid.NewGuid(), "pw"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── WorkspaceService.CreateWorkspaceAsync — transaction failure ────────────────

    [Fact]
    public async Task WorkspaceService_create_rolls_back_and_rethrows_on_save_failure()
    {
        var tx = new Mock<IUnitOfWorkTransaction>();
        var uow = UowThatFailsOnSave(tx);
        var wsRepo = new Mock<IWorkspaceRepository>();
        wsRepo.Setup(r => r.SlugExistsInOrgAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var memberRepo = new Mock<IWorkspaceMemberRepository>();

        var git = new Mock<Flowamaz.Core.Interfaces.Git.IWorkspaceGitService>();
        var service = new WorkspaceService(wsRepo.Object, memberRepo.Object, uow.Object, git.Object, NullLogger<WorkspaceService>.Instance);

        await service.Invoking(s => s.CreateWorkspaceAsync(Guid.NewGuid(), "Marketing", "marketing", Guid.NewGuid()))
            .Should().ThrowAsync<InvalidOperationException>();

        tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── AuthService.RefreshAsync — rotation failure rolls back ─────────────────────

    [Fact]
    public async Task AuthService_refresh_rolls_back_and_rethrows_when_rotation_save_fails()
    {
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = Guid.NewGuid(), Email = "ada@acme.test", Name = "Ada", IsActive = true };
        var token = new RefreshToken { OrgUserId = user.Id, TokenHash = "tok-hash", ExpiresAt = DateTime.UtcNow.AddDays(3) };

        var tx = new Mock<IUnitOfWorkTransaction>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(DbFault); // fails inside IssueTokensAsync

        var orgService = new Mock<IOrganisationService>();
        orgService.Setup(s => s.GetByIdAsync(user.OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = user.OrgId, Slug = "acme" });
        var orgUserService = new Mock<IOrgUserService>();
        var orgUserRepo = new Mock<IOrgUserRepository>();
        orgUserRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var memberRepo = new Mock<IWorkspaceMemberRepository>();
        memberRepo.Setup(r => r.GetActiveMembershipsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var refreshRepo = new Mock<IRefreshTokenRepository>();
        refreshRepo.Setup(r => r.GetByHashAsync("tok-hash", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.HashRefreshToken("tok")).Returns("tok-hash");
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<OrgUser>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceMembership>>())).Returns("access");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain", "hash"));

        var resetRepo = new Mock<IPasswordResetTokenRepository>();
        var email = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().Build();

        var service = new AuthService(
            new RegisterRequestValidator(), new LoginRequestValidator(),
            orgService.Object, orgUserService.Object, orgUserRepo.Object, memberRepo.Object,
            refreshRepo.Object, resetRepo.Object, jwt.Object, email.Object, uow.Object,
            Options.Create(new JwtOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 7 }),
            config,
            NullLogger<AuthService>.Instance);

        await service.Invoking(s => s.RefreshAsync("tok", "1.2.3.4"))
            .Should().ThrowAsync<InvalidOperationException>();

        tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
