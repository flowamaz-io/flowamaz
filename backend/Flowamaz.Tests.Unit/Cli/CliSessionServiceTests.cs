using FluentAssertions;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Cli;

/// <summary>
/// CLI sessions: created on device-flow approval, refreshed without rotation, ended by `fmz auth logout` or from
/// Settings — and ending one also revokes the access token it last handed out.
/// </summary>
public class CliSessionServiceTests
{
    internal readonly Mock<ICliSessionRepository> Sessions = new();
    internal readonly Mock<IOrgUserRepository> Users = new();
    internal readonly Mock<IOrganisationService> Orgs = new();
    internal readonly Mock<IWorkspaceMemberRepository> Members = new();
    internal readonly Mock<IJwtService> Jwt = new();
    internal readonly Mock<IUnitOfWork> Uow = new();
    internal readonly Mock<ITokenRevocationService> Revocation = new();
    internal readonly Mock<IAuditService> Audit = new();

    internal static readonly Guid OrgId = Guid.NewGuid();
    internal static readonly Guid UserId = Guid.NewGuid();
    internal readonly OrgUser User = new() { Id = UserId, OrgId = OrgId, Email = "ada@acme.test", Name = "Ada", IsActive = true };

    public CliSessionServiceTests()
    {
        Users.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => User);
        Orgs.Setup(s => s.GetByIdAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = OrgId, Name = "Acme", Slug = "acme" });
        Members.Setup(r => r.GetActiveMembershipsForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceMembership>());
        Jwt.Setup(j => j.GenerateAccessToken(It.IsAny<OrgUser>(), "acme", It.IsAny<IReadOnlyList<WorkspaceMembership>>()))
            .Returns("access-token");
        Jwt.Setup(j => j.ReadTokenIdentity("access-token")).Returns(("jti-1", DateTime.UtcNow.AddMinutes(15)));
        Jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-refresh", "hash-of-refresh"));
        Jwt.Setup(j => j.HashRefreshToken(It.IsAny<string>())).Returns<string>(p => "hash-of-" + p.Replace("plain-", ""));
    }

    internal CliSessionService Build(int idleDays = 30) => new(
        Sessions.Object, Users.Object, Orgs.Object, Members.Object, Jwt.Object, Uow.Object,
        Options.Create(new JwtOptions { CliSessionIdleDays = idleDays }),
        NullLogger<CliSessionService>.Instance, Revocation.Object, Audit.Object);

    internal CliSession LiveSession(Guid? owner = null) => new()
    {
        OrgUserId = owner ?? UserId,
        OrgId = OrgId,
        TokenHash = "hash-of-refresh",
        Name = "laptop",
        ExpiresAt = DateTime.UtcNow.AddDays(10),
        CurrentAccessTokenId = "jti-old",
        CurrentAccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(10),
    };

    [Fact]
    public async Task Create_stores_only_the_hash_and_returns_the_plain_token_once()
    {
        CliSession? stored = null;
        Sessions.Setup(r => r.AddAsync(It.IsAny<CliSession>(), It.IsAny<CancellationToken>()))
            .Callback<CliSession, CancellationToken>((s, _) => stored = s);

        var tokens = await Build(idleDays: 30).CreateAsync(UserId, "  jagan-mbp\n", "10.0.0.1");

        tokens.Should().NotBeNull();
        tokens!.RefreshToken.Should().Be("plain-refresh");
        tokens.AccessToken.Should().Be("access-token");
        stored.Should().NotBeNull();
        stored!.TokenHash.Should().Be("hash-of-refresh");
        stored.Name.Should().Be("jagan-mbp");
        stored.OrgId.Should().Be(OrgId);
        stored.CurrentAccessTokenId.Should().Be("jti-1");
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Audit.Verify(a => a.RecordAsync(It.Is<AuditEventRequest>(e => e.EventType == "cli.session.created"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_refuses_an_inactive_user()
    {
        User.IsActive = false;

        var tokens = await Build().CreateAsync(UserId, "laptop", "10.0.0.1");

        tokens.Should().BeNull();
        Sessions.Verify(r => r.AddAsync(It.IsAny<CliSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null, "fmz CLI")]
    [InlineData("   ", "fmz CLI")]
    [InlineData("\u0007\u0008", "fmz CLI")]
    [InlineData("build-agent-7", "build-agent-7")]
    public void CleanName_defaults_and_strips_control_characters(string? input, string expected) =>
        CliSessionService.CleanName(input).Should().Be(expected);

    [Fact]
    public void CleanName_caps_length() =>
        CliSessionService.CleanName(new string('x', 300)).Should().HaveLength(CliSessionService.MaxNameLength);

    [Fact]
    public async Task Refresh_issues_a_new_access_token_slides_expiry_and_does_not_rotate()
    {
        var session = LiveSession();
        Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var result = await Build(idleDays: 30).RefreshAsync("plain-refresh", "10.0.0.2");

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("access-token");
        session.TokenHash.Should().Be("hash-of-refresh", "the refresh token isn't rotated");
        session.CurrentAccessTokenId.Should().Be("jti-1");
        session.LastUsedIp.Should().Be("10.0.0.2");
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        Jwt.Verify(j => j.GenerateRefreshToken(), Times.Never);
    }

    [Fact]
    public async Task Refresh_refuses_unknown_revoked_and_expired_sessions()
    {
        var service = Build();
        (await service.RefreshAsync("plain-unknown", "ip")).Should().BeNull();
        (await service.RefreshAsync("", "ip")).Should().BeNull();

        var revoked = LiveSession();
        revoked.RevokedAt = DateTime.UtcNow.AddMinutes(-1);
        Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(revoked);
        (await service.RefreshAsync("plain-refresh", "ip")).Should().BeNull();

        var expired = LiveSession();
        expired.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(expired);
        (await service.RefreshAsync("plain-refresh", "ip")).Should().BeNull();

        Jwt.Verify(j => j.GenerateAccessToken(It.IsAny<OrgUser>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceMembership>>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_for_a_deactivated_user_ends_the_session()
    {
        var session = LiveSession();
        Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(session);
        User.IsActive = false;

        var result = await Build().RefreshAsync("plain-refresh", "ip");

        result.Should().BeNull();
        session.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_ends_the_session_and_revokes_its_current_access_token()
    {
        var session = LiveSession();
        Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var ended = await Build().LogoutAsync("plain-refresh");

        ended.Should().BeTrue();
        session.RevokedAt.Should().NotBeNull();
        Revocation.Verify(r => r.RevokeTokenAsync("jti-old", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logout_with_an_unknown_or_ended_token_is_a_no_op()
    {
        (await Build().LogoutAsync("plain-unknown")).Should().BeFalse();
        (await Build().LogoutAsync(null)).Should().BeFalse();
        Revocation.Verify(r => r.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_only_finds_the_callers_own_session()
    {
        var sessionId = Guid.NewGuid();
        // The repository is asked with the caller's id; someone else's session id simply isn't found.
        Sessions.Setup(r => r.GetForUserAsync(UserId, sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((CliSession?)null);

        var revoked = await Build().RevokeAsync(UserId, sessionId);

        revoked.Should().BeFalse();
        Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_ends_the_session_cuts_off_its_token_and_audits()
    {
        var session = LiveSession();
        Sessions.Setup(r => r.GetForUserAsync(UserId, session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var revoked = await Build().RevokeAsync(UserId, session.Id);

        revoked.Should().BeTrue();
        session.RevokedAt.Should().NotBeNull();
        Revocation.Verify(r => r.RevokeTokenAsync("jti-old", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Audit.Verify(a => a.RecordAsync(It.Is<AuditEventRequest>(e => e.EventType == "cli.session.revoked" && e.ResourceId == session.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_skips_token_revocation_when_the_last_token_already_expired()
    {
        var session = LiveSession();
        session.CurrentAccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        Sessions.Setup(r => r.GetForUserAsync(UserId, session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        (await Build().RevokeAsync(UserId, session.Id)).Should().BeTrue();

        Revocation.Verify(r => r.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_marks_the_session_making_the_call()
    {
        var mine = LiveSession();
        mine.CurrentAccessTokenId = "jti-current";
        var other = LiveSession();
        Sessions.Setup(r => r.ListActiveForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CliSession> { mine, other });

        var list = await Build().ListAsync(UserId, "jti-current");

        list.Should().HaveCount(2);
        list.Single(s => s.Id == mine.Id).IsCurrent.Should().BeTrue();
        list.Single(s => s.Id == other.Id).IsCurrent.Should().BeFalse();
    }
}
