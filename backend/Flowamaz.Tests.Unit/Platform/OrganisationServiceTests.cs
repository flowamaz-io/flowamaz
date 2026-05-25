using Flowamaz.Application.Platform.Services;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Platform;

public class OrganisationServiceTests
{
    private readonly Mock<IPlanRepository> _planRepo = new();
    private readonly Mock<IOrganisationRepository> _orgRepo = new();
    private readonly Mock<IOrgUserRepository> _orgUserRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<IEmailService> _email = new();

    private readonly Plan _communityPlan = new()
    {
        Id = Guid.NewGuid(),
        Name = "Community",
        Slug = "community",
        Limits = new PlanLimits { MaxRunsPerMonth = 500, AllowedAiProviders = ["anthropic"] },
    };

    private OrganisationService CreateService()
    {
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new OrganisationService(
            _planRepo.Object, _orgRepo.Object, _orgUserRepo.Object,
            _uow.Object, _email.Object, NullLogger<OrganisationService>.Instance);
    }

    [Fact]
    public async Task RegisterOrganisationAsync_happy_path_creates_org_owner_and_trial_subscription()
    {
        Organisation? capturedOrg = null;
        OrgUser? capturedOwner = null;
        Subscription? capturedSub = null;

        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgRepo.Setup(o => o.AddAsync(It.IsAny<Organisation>(), It.IsAny<CancellationToken>()))
            .Callback<Organisation, CancellationToken>((o, _) => capturedOrg = o).Returns(Task.CompletedTask);
        _orgUserRepo.Setup(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()))
            .Callback<OrgUser, CancellationToken>((u, _) => capturedOwner = u).Returns(Task.CompletedTask);
        _orgRepo.Setup(o => o.AddSubscriptionAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
            .Callback<Subscription, CancellationToken>((s, _) => capturedSub = s).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        var result = await service.RegisterOrganisationAsync(
            name: "Acme Corp", slug: "Acme-Corp", billingEmail: "billing@acme.test",
            ownerEmail: "owner@acme.test", ownerName: "Ada Owner", ownerPassword: "Sup3rSecret!",
            planSlug: "community", dataRegion: DataRegion.EuWest1);

        result.OrgSlug.Should().Be("acme-corp");
        result.OwnerEmail.Should().Be("owner@acme.test");
        result.PlanSlug.Should().Be("community");
        result.Status.Should().Be(OrgStatus.Trial);
        result.TrialEndsAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));

        capturedOrg!.Status.Should().Be(OrgStatus.Trial);
        capturedOrg.PlanId.Should().Be(_communityPlan.Id);
        capturedOrg.DataRegion.Should().Be(DataRegion.EuWest1);
        capturedSub!.OrgId.Should().Be(capturedOrg.Id);
        capturedSub.Status.Should().Be(SubscriptionStatus.Active);

        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendAsync("owner@acme.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterOrganisationAsync_hashes_password_with_bcrypt_cost_12_and_never_stores_plaintext()
    {
        OrgUser? capturedOwner = null;
        const string plaintext = "Sup3rSecret!";

        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgUserRepo.Setup(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()))
            .Callback<OrgUser, CancellationToken>((u, _) => capturedOwner = u).Returns(Task.CompletedTask);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        await service.RegisterOrganisationAsync(
            "Acme", "acme", "billing@acme.test", "owner@acme.test", "Ada", plaintext, "community", DataRegion.ApSoutheast1);

        capturedOwner!.PasswordHash.Should().NotBe(plaintext);
        capturedOwner.PasswordHash.Should().Contain("$12$"); // bcrypt work factor 12
        BCrypt.Net.BCrypt.Verify(plaintext, capturedOwner.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterOrganisationAsync_duplicate_slug_throws_and_never_opens_transaction()
    {
        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync("taken", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = CreateService();

        var act = () => service.RegisterOrganisationAsync(
            "Acme", "taken", "billing@acme.test", "owner@acme.test", "Ada", "Sup3rSecret!", "community", DataRegion.ApSoutheast1);

        await act.Should().ThrowAsync<SlugAlreadyExistsException>();
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterOrganisationAsync_allows_same_owner_email_across_different_orgs()
    {
        // Email is unique per org, not globally (FUNCTIONAL.md §4): two orgs may share an owner email.
        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        var first = await service.RegisterOrganisationAsync(
            "Acme One", "acme-one", "b@acme.test", "shared@owner.test", "Ada", "Sup3rSecret!", "community", DataRegion.ApSoutheast1);
        var second = await service.RegisterOrganisationAsync(
            "Acme Two", "acme-two", "b@acme.test", "shared@owner.test", "Ada", "Sup3rSecret!", "community", DataRegion.ApSoutheast1);

        first.OwnerEmail.Should().Be("shared@owner.test");
        second.OwnerEmail.Should().Be("shared@owner.test");
        first.OrgId.Should().NotBe(second.OrgId);
    }

    [Fact]
    public async Task RegisterOrganisationAsync_rolls_back_when_persistence_fails()
    {
        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated db failure"));

        var service = new OrganisationService(
            _planRepo.Object, _orgRepo.Object, _orgUserRepo.Object,
            _uow.Object, _email.Object, NullLogger<OrganisationService>.Instance);

        var act = () => service.RegisterOrganisationAsync(
            "Acme", "acme", "billing@acme.test", "owner@acme.test", "Ada", "Sup3rSecret!", "community", DataRegion.ApSoutheast1);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterOrganisationAsync_succeeds_even_when_welcome_email_fails()
    {
        _planRepo.Setup(p => p.GetBySlugAsync("community", It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);
        _orgRepo.Setup(o => o.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // email provider down — must not roll back registration

        var service = CreateService();

        var result = await service.RegisterOrganisationAsync(
            "Acme", "acme", "billing@acme.test", "owner@acme.test", "Ada", "Sup3rSecret!", "community", DataRegion.ApSoutheast1);

        result.Should().NotBeNull();
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPlanLimitsAsync_returns_limits_of_the_orgs_plan()
    {
        var orgId = Guid.NewGuid();
        var org = new Organisation { Id = orgId, PlanId = _communityPlan.Id, Slug = "acme" };
        _orgRepo.Setup(o => o.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _planRepo.Setup(p => p.GetByIdAsync(_communityPlan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_communityPlan);

        var service = CreateService();

        var limits = await service.GetPlanLimitsAsync(orgId);

        limits.MaxRunsPerMonth.Should().Be(500);
        limits.AllowedAiProviders.Should().ContainSingle().Which.Should().Be("anthropic");
    }
}
