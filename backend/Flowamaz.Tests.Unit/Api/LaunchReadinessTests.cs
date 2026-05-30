using System.IO;
using FluentAssertions;
using Flowamaz.Api.Cors;
using Xunit;

namespace Flowamaz.Tests.Unit.Api;

/// <summary>
/// Launch-readiness guards (prompt 08-05): CORS origin resolution and the presence of the required
/// nginx security headers + rate-limit zones.
/// </summary>
public sealed class LaunchReadinessTests
{
    [Fact]
    public void Cors_production_uses_only_configured_origins_never_localhost()
    {
        var configured = new[] { "https://app.flowamaz.io" };

        var origins = CorsOriginPolicy.Resolve(isDevelopment: false, configured, marketingSiteUrl: null);

        origins.Should().BeEquivalentTo(["https://app.flowamaz.io"]);
        origins.Should().NotContain(o => o.Contains("localhost"));
    }

    [Fact]
    public void Cors_production_with_no_configured_origins_blocks_everything()
    {
        var origins = CorsOriginPolicy.Resolve(isDevelopment: false, [], marketingSiteUrl: null);
        origins.Should().BeEmpty();
    }

    [Fact]
    public void Cors_includes_marketing_site_when_configured()
    {
        var origins = CorsOriginPolicy.Resolve(
            isDevelopment: false, ["https://app.flowamaz.io"], "https://flowamaz.com");

        origins.Should().Contain("https://flowamaz.com");
    }

    [Fact]
    public void Cors_development_falls_back_to_localhost()
    {
        var origins = CorsOriginPolicy.Resolve(isDevelopment: true, [], marketingSiteUrl: null);
        origins.Should().Contain("http://localhost:5173");
    }

    [Theory]
    [InlineData("Content-Security-Policy")]
    [InlineData("X-Frame-Options")]
    [InlineData("X-Content-Type-Options")]
    [InlineData("Strict-Transport-Security")]
    [InlineData("Referrer-Policy")]
    [InlineData("Permissions-Policy")]
    [InlineData("Cross-Origin-Opener-Policy")]
    [InlineData("Cross-Origin-Resource-Policy")]
    public void Nginx_config_declares_required_security_header(string header)
    {
        ReadNginxConfig().Should().Contain(header);
    }

    [Fact]
    public void Nginx_config_declares_webhook_rate_zone()
    {
        ReadNginxConfig().Should().Contain("zone=webhook");
    }

    // Walks up from the test assembly to the repo root (the dir containing "infrastructure").
    private static string ReadNginxConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "infrastructure")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the repo root containing 'infrastructure/' must be reachable from the test output dir");
        var path = Path.Combine(dir!.FullName, "infrastructure", "nginx", "nginx.conf");
        File.Exists(path).Should().BeTrue($"nginx.conf should exist at {path}");
        return File.ReadAllText(path);
    }
}
