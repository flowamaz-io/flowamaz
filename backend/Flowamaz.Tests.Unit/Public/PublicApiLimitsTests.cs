using FluentAssertions;
using Flowamaz.Api.Controllers.Public;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Tests.Unit.Public;

/// <summary>
/// Public API budgets: gate decisions have their own bucket so an app that runs approvals through the API
/// (several decisions per run) doesn't use up the budget for starting runs. All three are configurable.
/// </summary>
public class PublicApiLimitsTests
{
    [Fact]
    public void Defaults_give_decisions_their_own_budget()
    {
        var limits = new PublicApiLimits();
        limits.PerHour("read").Should().Be(1000);
        limits.PerHour("trigger").Should().Be(100);
        limits.PerHour("decide").Should().Be(1000);
    }

    [Fact]
    public void Budgets_bind_from_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicApi:TriggerPerHour"] = "500",
            ["PublicApi:DecidePerHour"] = "5000",
        }).Build();

        var limits = config.GetSection(PublicApiLimits.Section).Get<PublicApiLimits>()!;

        limits.PerHour("trigger").Should().Be(500);
        limits.PerHour("decide").Should().Be(5000);
        limits.PerHour("read").Should().Be(1000);
    }

    [Fact]
    public void Unknown_bucket_falls_back_to_the_read_budget() =>
        new PublicApiLimits { ReadPerHour = 42 }.PerHour("anything").Should().Be(42);
}
