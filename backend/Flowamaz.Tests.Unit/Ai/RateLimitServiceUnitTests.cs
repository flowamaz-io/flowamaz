using FluentAssertions;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Flowamaz.Tests.Unit.Ai;

/// <summary>
/// Unit coverage for the rate limiter's argument guards and the fail-open contract. The atomic
/// INCR+EXPIRE behaviour against real Redis lives in the integration suite (Testcontainers).
/// </summary>
public class RateLimitServiceUnitTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();

    private RateLimitService NewService()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);
        return new RateLimitService(_redis.Object, NullLogger<RateLimitService>.Instance);
    }

    [Fact]
    public async Task CheckAndIncrementAsync_redis_down_fails_open_returns_true()
    {
        var service = NewService();
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        (await service.CheckAndIncrementAsync("user:1", maxCount: 60, windowSeconds: 3600))
            .Should().BeTrue("a Redis outage must not lock all users out — fail open");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CheckAndIncrementAsync_invalid_maxCount_throws(int maxCount)
    {
        var service = NewService();
        await service.Invoking(s => s.CheckAndIncrementAsync("user:1", maxCount, 3600))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_invalid_window_throws()
    {
        var service = NewService();
        await service.Invoking(s => s.CheckAndIncrementAsync("user:1", 60, windowSeconds: 0))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_empty_key_throws()
    {
        var service = NewService();
        await service.Invoking(s => s.CheckAndIncrementAsync("", 60, 3600))
            .Should().ThrowAsync<ArgumentException>();
    }
}
