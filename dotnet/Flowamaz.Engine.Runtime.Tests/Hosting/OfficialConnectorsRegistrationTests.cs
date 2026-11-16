using FluentAssertions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Engine.Runtime.Tests.Hosting;

/// <summary>AddFlowamazOfficialConnectors makes the official connector operations available to connector steps.</summary>
public sealed class OfficialConnectorsRegistrationTests
{
    [Fact]
    public void The_official_operations_resolve_once_registered()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();
        services.AddFlowamazEngineRuntime(configuration);
        services.AddFlowamazOfficialConnectors(configuration);
        services.AddFlowamazOfficialConnectors(configuration); // twice is harmless
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IConnectorOperationHandlerRegistry>();
        registry.Get("slack", "send-message").Should().NotBeNull();
        registry.Get("http-rest", "GET").Should().NotBeNull();
        registry.Get("slack", "nope").Should().BeNull();
        scope.ServiceProvider.GetServices<IConnectorOperationHandler>().Select(h => (h.ConnectorId, h.OperationId))
            .Should().OnlyHaveUniqueItems();
    }
}
