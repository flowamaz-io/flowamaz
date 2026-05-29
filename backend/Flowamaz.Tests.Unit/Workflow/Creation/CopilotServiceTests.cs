using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class CopilotServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-abc";

    private static ModelConfig TestConfig() =>
        new("claude-haiku-4-5", "anthropic", AiKeySource.Platform, "key", false, 200_000, true, true);

    private CopilotService Build(
        Mock<IRateLimitService>? rateLimit = null,
        Mock<IAiBudgetService>? budget = null,
        Mock<ICopilotPatternMatcher>? matcher = null,
        Mock<ISemanticCacheService>? cache = null,
        Mock<IModelResolutionService>? resolution = null,
        Mock<IAiCompletionService>? ai = null,
        Mock<IAiTokenMeteringService>? metering = null)
    {
        rateLimit ??= AllowedRateLimit();
        budget ??= BudgetAvailable();
        matcher ??= NoMatch();
        cache ??= CacheMiss();
        resolution ??= Resolver();
        ai ??= AiReturns("patch: value");
        metering ??= new Mock<IAiTokenMeteringService>();

        return new CopilotService(rateLimit.Object, budget.Object, matcher.Object,
            cache.Object, resolution.Object, ai.Object, metering.Object,
            NullLogger<CopilotService>.Instance);
    }

    private static Mock<IRateLimitService> AllowedRateLimit()
    {
        var m = new Mock<IRateLimitService>();
        m.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return m;
    }

    private static Mock<IRateLimitService> BlockedRateLimit()
    {
        var m = new Mock<IRateLimitService>();
        m.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return m;
    }

    private static Mock<IAiBudgetService> BudgetAvailable()
    {
        var m = new Mock<IAiBudgetService>();
        m.Setup(b => b.IsBudgetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return m;
    }

    private static Mock<IAiBudgetService> BudgetExhausted()
    {
        var m = new Mock<IAiBudgetService>();
        m.Setup(b => b.IsBudgetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return m;
    }

    private static Mock<ICopilotPatternMatcher> NoMatch()
    {
        var m = new Mock<ICopilotPatternMatcher>();
        m.Setup(p => p.TryMatch(It.IsAny<string>(), It.IsAny<string?>())).Returns((PatternMatchResult?)null);
        return m;
    }

    private static Mock<ICopilotPatternMatcher> PatternHit(string name, string patch)
    {
        var m = new Mock<ICopilotPatternMatcher>();
        m.Setup(p => p.TryMatch(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new PatternMatchResult(name, patch, "desc"));
        return m;
    }

    private static Mock<ISemanticCacheService> CacheMiss()
    {
        var m = new Mock<ISemanticCacheService>();
        m.Setup(c => c.ComputeKey(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns("k");
        m.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        m.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return m;
    }

    private static Mock<ISemanticCacheService> CacheHit(string value)
    {
        var m = new Mock<ISemanticCacheService>();
        m.Setup(c => c.ComputeKey(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns("k");
        m.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(value);
        return m;
    }

    private static Mock<IModelResolutionService> Resolver()
    {
        var m = new Mock<IModelResolutionService>();
        m.Setup(r => r.ResolveModelConfigAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestConfig());
        return m;
    }

    private static Mock<IAiCompletionService> AiReturns(string text)
    {
        var m = new Mock<IAiCompletionService>();
        m.Setup(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(text, 100, 50));
        return m;
    }

    [Fact]
    public async Task RateLimited_Returns_Failure()
    {
        var svc = Build(rateLimit: BlockedRateLimit());

        var result = await svc.ProcessCommandAsync("add step", null, WorkspaceId, UserId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Rate limit");
    }

    [Fact]
    public async Task BudgetExhausted_Returns_Failure()
    {
        var svc = Build(budget: BudgetExhausted());

        var result = await svc.ProcessCommandAsync("add step", null, WorkspaceId, UserId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("budget");
    }

    [Fact]
    public async Task PatternMatch_Returns_Patch_Without_AI()
    {
        var ai = new Mock<IAiCompletionService>();
        var svc = Build(matcher: PatternHit("add-step", "step: value"), ai: ai);

        var result = await svc.ProcessCommandAsync("add step Approve Invoice", null, WorkspaceId, UserId);

        result.Success.Should().BeTrue();
        result.MatchedPattern.Should().Be("add-step");
        result.YamlPatch.Should().Be("step: value");
        result.TokensUsed.Should().Be(0);
        ai.Verify(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CacheHit_Returns_Cached_Patch_Without_AI()
    {
        var ai = new Mock<IAiCompletionService>();
        var svc = Build(cache: CacheHit("cached-patch: x"), ai: ai);

        var result = await svc.ProcessCommandAsync("rename step", null, WorkspaceId, UserId);

        result.Success.Should().BeTrue();
        result.CacheHit.Should().BeTrue();
        result.YamlPatch.Should().Be("cached-patch: x");
        ai.Verify(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CacheMiss_Calls_AI_And_Meters()
    {
        var metering = new Mock<IAiTokenMeteringService>();
        var svc = Build(metering: metering);

        var result = await svc.ProcessCommandAsync("do something custom", null, WorkspaceId, UserId);

        result.Success.Should().BeTrue();
        result.YamlPatch.Should().Be("patch: value");
        result.CacheHit.Should().BeFalse();
        metering.Verify(m => m.RecordUsage(
            AiFunctionIds.Copilot, It.IsAny<string>(), It.IsAny<string>(), null, WorkspaceId,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Once);
    }
}
