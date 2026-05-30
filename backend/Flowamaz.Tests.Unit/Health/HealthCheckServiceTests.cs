using System.Reflection;
using Flowamaz.Api.Controllers;
using Flowamaz.Application.Health;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HealthCheckService = Flowamaz.Application.Health.HealthCheckService;

namespace Flowamaz.Tests.Unit.Health;

/// <summary>
/// Unit coverage for the /health response composition and its critical-vs-non-critical contract,
/// plus the response-cache contract on the connector catalogue endpoint (prompt 07-06).
/// </summary>
public sealed class HealthCheckServiceTests
{
    private static HealthReportEntry Entry(HealthStatus status, double ms) =>
        new(status, description: null, duration: TimeSpan.FromMilliseconds(ms), exception: null, data: null);

    private static HealthReport Report(params (string Name, HealthStatus Status, double Ms)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Name, e => Entry(e.Status, e.Ms));
        var total = TimeSpan.FromMilliseconds(entries.Sum(e => e.Ms));
        return new HealthReport(dict, total);
    }

    [Fact]
    public void Compose_AllDependenciesHealthy_ReportsHealthyWith200()
    {
        var report = Report(
            ("database", HealthStatus.Healthy, 4),
            ("redis", HealthStatus.Healthy, 1),
            ("stripe", HealthStatus.Healthy, 145),
            ("anthropic", HealthStatus.Healthy, 312),
            ("resend", HealthStatus.Healthy, 89));

        var summary = HealthCheckService.Compose(report, "1.0.0", "Production");

        summary.Status.Should().Be("healthy");
        summary.Version.Should().Be("1.0.0");
        summary.Environment.Should().Be("Production");
        summary.Dependencies.Should().HaveCount(5);
        summary.Dependencies["database"].Status.Should().Be("healthy");
        summary.Dependencies["database"].ResponseMs.Should().Be(4);
        HealthCheckService.ToHttpStatusCode(report.Status).Should().Be(200);
    }

    [Fact]
    public void Compose_DatabaseDown_ReportsUnhealthyWith503()
    {
        // Database is critical → its Unhealthy makes the whole report Unhealthy → 503.
        var report = Report(
            ("database", HealthStatus.Unhealthy, 2001),
            ("redis", HealthStatus.Healthy, 1));

        var summary = HealthCheckService.Compose(report, "1.0.0", "Production");

        summary.Status.Should().Be("unhealthy");
        summary.Dependencies["database"].Status.Should().Be("unhealthy");
        HealthCheckService.ToHttpStatusCode(report.Status).Should().Be(503);
    }

    [Fact]
    public void Compose_RedisDown_ReportsUnhealthyWith503()
    {
        // Redis is critical → its Unhealthy makes the whole report Unhealthy → 503.
        var report = Report(
            ("database", HealthStatus.Healthy, 4),
            ("redis", HealthStatus.Unhealthy, 2001));

        var summary = HealthCheckService.Compose(report, "1.0.0", "Production");

        summary.Status.Should().Be("unhealthy");
        summary.Dependencies["redis"].Status.Should().Be("unhealthy");
        HealthCheckService.ToHttpStatusCode(report.Status).Should().Be(503);
    }

    [Fact]
    public void Compose_ExternalDependencyDegraded_StaysHealthyish200()
    {
        // A degraded third party (Stripe) is NON-critical: report is at worst Degraded → still HTTP 200.
        var report = Report(
            ("database", HealthStatus.Healthy, 4),
            ("redis", HealthStatus.Healthy, 1),
            ("stripe", HealthStatus.Degraded, 3000));

        var summary = HealthCheckService.Compose(report, "1.0.0", "Production");

        summary.Status.Should().Be("degraded");
        summary.Dependencies["stripe"].Status.Should().Be("degraded");
        HealthCheckService.ToHttpStatusCode(report.Status).Should().Be(200);
    }

    [Fact]
    public void ConnectorCatalogue_List_IsResponseCachedForOneHour()
    {
        // The connector catalogue list endpoint must be cached for 1h, varied per Authorization.
        var action = typeof(ConnectorsController).GetMethod(nameof(ConnectorsController.List));
        action.Should().NotBeNull();

        var cache = action!.GetCustomAttribute<ResponseCacheAttribute>();
        cache.Should().NotBeNull("the connector catalogue list must declare a ResponseCache policy");
        cache!.Duration.Should().Be(3600);
        cache.VaryByHeader.Should().Be("Authorization");
    }

    [Fact]
    public void BillingPlans_GetPlans_IsResponseCachedForOneDay()
    {
        var action = typeof(BillingController).GetMethod(nameof(BillingController.GetPlans));
        action.Should().NotBeNull();

        var cache = action!.GetCustomAttribute<ResponseCacheAttribute>();
        cache.Should().NotBeNull("plan definitions must declare a ResponseCache policy");
        cache!.Duration.Should().Be(86400);
    }
}
