using FluentAssertions;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Infra;

/// <summary>
/// Unit-tests the startup migration policy against a mocked <see cref="IMigrationRunner"/>:
/// already-applied is a no-op, pending migrations are applied once, and a DB failure re-throws so
/// the app never starts against a bad schema (prompt 02-01).
/// </summary>
public class StartupMigrationServiceTests
{
    private static StartupMigrationService NewService(IMigrationRunner runner) =>
        new(runner, new ConfigurationBuilder().Build(), NullLogger<StartupMigrationService>.Instance);

    [Fact]
    public async Task RunAsync_is_noop_when_no_pending_migrations()
    {
        var runner = new Mock<IMigrationRunner>();
        runner.Setup(r => r.GetPendingMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        await NewService(runner.Object).RunAsync();

        runner.Verify(r => r.MigrateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_applies_pending_migrations_once()
    {
        var runner = new Mock<IMigrationRunner>();
        runner.Setup(r => r.GetPendingMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "20260525_AddWorkflowSchema", "20260526_Next" });

        await NewService(runner.Object).RunAsync();

        runner.Verify(r => r.MigrateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_rethrows_when_database_is_unavailable()
    {
        var runner = new Mock<IMigrationRunner>();
        runner.Setup(r => r.GetPendingMigrationsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection refused"));

        var act = () => NewService(runner.Object).RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("connection refused");
    }
}
