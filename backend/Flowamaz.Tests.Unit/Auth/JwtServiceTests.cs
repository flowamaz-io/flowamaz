using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flowamaz.Tests.Unit.Auth;

public class JwtServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Secret = "unit-test-jwt-secret-at-least-32-bytes-long-x",
        Issuer = "flowamaz-api",
        Audience = "flowamaz-client",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7,
    };

    private static JwtService CreateService() =>
        new(OptionsWrap(Options), NullLogger<JwtService>.Instance);

    private static IOptions<JwtOptions> OptionsWrap(JwtOptions o) => Microsoft.Extensions.Options.Options.Create(o);

    [Fact]
    public void Constructor_throws_when_secret_too_short()
    {
        var act = () => new JwtService(OptionsWrap(new JwtOptions { Secret = "short" }), NullLogger<JwtService>.Instance);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenerateAccessToken_then_validate_round_trips_identity_and_memberships()
    {
        var service = CreateService();
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = Guid.NewGuid(), Email = "ada@acme.test", Name = "Ada" };
        var wsId = Guid.NewGuid();
        var memberships = new List<WorkspaceMembership>
        {
            new(wsId, "finops", "Fin Ops", "Admin", 5),
        };

        var token = service.GenerateAccessToken(user, "acme", memberships);
        var principal = service.ValidateAccessToken(token);

        principal.Should().NotBeNull();
        service.ExtractOrgUserId(principal!).Should().Be(user.Id);
        service.ExtractOrgId(principal!).Should().Be(user.OrgId);

        var extracted = service.ExtractWorkspaceMemberships(principal!);
        extracted.Should().ContainSingle();
        extracted[0].WorkspaceId.Should().Be(wsId);
        extracted[0].WorkspaceSlug.Should().Be("finops");
        extracted[0].RoleValue.Should().Be(5);
    }

    [Fact]
    public void ValidateAccessToken_returns_null_for_garbage()
    {
        var service = CreateService();
        service.ValidateAccessToken("not-a-jwt").Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_rejects_token_signed_with_a_different_secret()
    {
        var issuer = CreateService();
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = Guid.NewGuid(), Email = "a@b.test", Name = "A" };
        var token = issuer.GenerateAccessToken(user, "acme", []);

        var otherKeyService = new JwtService(
            OptionsWrap(new JwtOptions { Secret = "a-totally-different-secret-32-bytes-min-yyyy", Issuer = "flowamaz-api", Audience = "flowamaz-client" }),
            NullLogger<JwtService>.Instance);

        otherKeyService.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void GenerateRefreshToken_returns_hex_plain_and_matching_sha256_hash()
    {
        var service = CreateService();

        var (plain, hash) = service.GenerateRefreshToken();

        plain.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        hash.Should().Be(service.HashRefreshToken(plain));
        hash.Should().HaveLength(64);
        hash.Should().NotBe(plain);
    }
}
