using System.Diagnostics;
using FluentAssertions;
using Flowamaz.Core.Constants;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Ai;

public class AiTokenMeteringServiceTests
{
    private static (AiTokenMeteringService Service, IServiceScopeFactory ScopeFactory, string DbName) BuildService()
    {
        var dbName = $"meter-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return (new AiTokenMeteringService(scopeFactory, NullLogger<AiTokenMeteringService>.Instance), scopeFactory, dbName);
    }

    [Fact]
    public void RecordUsage_returns_immediately_does_not_block_caller()
    {
        var (service, _, _) = BuildService();

        var sw = Stopwatch.StartNew();
        service.RecordUsage(
            functionId: AiFunctionIds.Copilot,
            modelId: "claude-haiku-4-5",
            provider: AiProviders.Anthropic,
            orgId: null,
            workspaceId: Guid.NewGuid(),
            tokensInput: 100,
            tokensOutput: 200,
            costUsd: 0.001m);
        sw.Stop();

        // The DB write happens on Task.Run — caller must not wait. 50ms is generous;
        // in practice this returns in well under 1ms on the bench.
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "metering must never sit in the request path (FUNCTIONAL.md §13.3)");
    }

    [Fact]
    public async Task RecordUsage_persists_record_eventually()
    {
        var (service, scopeFactory, _) = BuildService();
        var workspaceId = Guid.NewGuid();

        service.RecordUsage(
            functionId: AiFunctionIds.Copilot,
            modelId: "claude-haiku-4-5",
            provider: AiProviders.Anthropic,
            orgId: null,
            workspaceId: workspaceId,
            tokensInput: 100,
            tokensOutput: 200,
            costUsd: 0.001m);

        // Wait up to 2s for the background write — generous because CI machines vary.
        await WaitForCondition(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            return await db.AiTokenUsage.AnyAsync(u => u.WorkspaceId == workspaceId);
        }, TimeSpan.FromSeconds(2));

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var stored = await verifyDb.AiTokenUsage.SingleAsync(u => u.WorkspaceId == workspaceId);
        stored.TokensInput.Should().Be(100);
        stored.TokensOutput.Should().Be(200);
        stored.CostUsd.Should().Be(0.001m);
        stored.FunctionId.Should().Be(AiFunctionIds.Copilot);
        stored.ModelId.Should().Be("claude-haiku-4-5");
    }

    private static async Task WaitForCondition(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Condition not met within {timeout.TotalMilliseconds}ms");
    }
}
