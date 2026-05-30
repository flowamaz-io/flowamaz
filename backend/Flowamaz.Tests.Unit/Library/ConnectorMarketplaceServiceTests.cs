using Flowamaz.Application.Library.Services;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Flowamaz.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Library;

public class ConnectorMarketplaceServiceTests
{
    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"marketplace-{Guid.NewGuid():N}")
            .Options);

    private static async Task<ConnectorDefinition> SeedConnectorAsync(FlowAmazDbContext db)
    {
        var def = new ConnectorDefinition
        {
            Id = Guid.NewGuid(), ConnectorId = "http-rest", DisplayName = "HTTP/REST",
            PublisherId = "flowamaz-io", Version = "1.0.0", Category = "generic",
            ManifestJson = "{}", Tier = ConnectorTier.Official, IsEnabled = true, IsOfficial = true,
        };
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def;
    }

    private static ConnectorMarketplaceService NewService(FlowAmazDbContext db, Mock<IConnectorSubmissionPrService>? pr = null)
    {
        pr ??= new Mock<IConnectorSubmissionPrService>();
        return new ConnectorMarketplaceService(
            new ConnectorMarketplaceRepository(db), pr.Object, new EfUnitOfWork(db),
            NullLogger<ConnectorMarketplaceService>.Instance);
    }

    [Fact]
    public async Task RateConnector_stores_rating_and_updates_average()
    {
        var db = NewDb();
        await SeedConnectorAsync(db);
        var service = NewService(db);

        await service.RateConnectorAsync("http-rest", Guid.NewGuid(), 4, "Solid");
        var result = await service.RateConnectorAsync("http-rest", Guid.NewGuid(), 2, null);

        result.RatingCount.Should().Be(2);
        result.AverageRating.Should().Be(3.0m);
    }

    [Fact]
    public async Task RateConnector_duplicate_org_updates_existing_rating()
    {
        var db = NewDb();
        await SeedConnectorAsync(db);
        var service = NewService(db);
        var org = Guid.NewGuid();

        await service.RateConnectorAsync("http-rest", org, 5, "Great");
        var result = await service.RateConnectorAsync("http-rest", org, 2, "Changed my mind");

        result.RatingCount.Should().Be(1);
        result.AverageRating.Should().Be(2.0m);
    }

    [Fact]
    public async Task SubmitConnector_valid_manifest_creates_github_pr()
    {
        var db = NewDb();
        var pr = new Mock<IConnectorSubmissionPrService>();
        pr.Setup(p => p.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://github.com/flowamaz-io/connectors/pull/42");
        var service = NewService(db, pr);

        var result = await service.SubmitConnectorAsync(Guid.NewGuid(), "My Connector", "id: my-connector\nversion: 1.0.0");

        result.GithubPrUrl.Should().Be("https://github.com/flowamaz-io/connectors/pull/42");
        result.Status.Should().Be("pending");
        pr.Verify(p => p.CreatePullRequestAsync("My Connector", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await db.ConnectorSubmissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SubmitConnector_invalid_manifest_throws_before_pr()
    {
        var db = NewDb();
        var pr = new Mock<IConnectorSubmissionPrService>();
        var service = NewService(db, pr);

        var act = () => service.SubmitConnectorAsync(Guid.NewGuid(), "Bad", "key: value:\n  - broken: : :");

        await act.Should().ThrowAsync<InvalidOperationException>();
        pr.Verify(p => p.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_increments_install_count_atomically()
    {
        var db = NewDb();
        var def = await SeedConnectorAsync(db);
        var catalogue = new ConnectorCatalogueService(db, NullLogger<ConnectorCatalogueService>.Instance);

        await catalogue.InstallAsync(Guid.NewGuid(), def.Id, credentialId: null, installedBy: Guid.NewGuid());

        var reloaded = await db.ConnectorDefinitions.AsNoTracking().FirstAsync(c => c.Id == def.Id);
        reloaded.InstallCount.Should().Be(1);
    }
}
