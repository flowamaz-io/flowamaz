using FluentAssertions;
using Flowamaz.Core.Constants;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Flowamaz.Tests.Unit.Ai;

/// <summary>
/// Unit-tests the semantic cache against a mocked Redis. Covers key determinism, the hit/miss
/// read path, and the fail-open contract (a Redis fault must surface as a miss, never throw).
/// </summary>
public class SemanticCacheServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Guid _workspaceId = Guid.NewGuid();

    private SemanticCacheService NewService()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);
        return new SemanticCacheService(_redis.Object, NullLogger<SemanticCacheService>.Instance);
    }

    // ── ComputeKey ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeKey_same_inputs_returns_same_hash()
    {
        var service = NewService();
        var a = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "Add a timeout");
        var b = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "Add a timeout");
        a.Should().Be(b);
        a.Should().StartWith("aicache:");
    }

    [Fact]
    public void ComputeKey_different_commands_returns_different_hash()
    {
        var service = NewService();
        var a = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "Add a timeout");
        var b = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "Remove the retry");
        a.Should().NotBe(b);
    }

    [Fact]
    public void ComputeKey_normalises_whitespace_and_case()
    {
        var service = NewService();
        var a = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "Add timeout");
        var b = service.ComputeKey(AiFunctionIds.Copilot, _workspaceId, "  add   TIMEOUT ");
        a.Should().Be(b, "normalisation lowercases and collapses whitespace before hashing");
    }

    // ── GetAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_cache_hit_returns_value()
    {
        var service = NewService();
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"cached-response");

        (await service.GetAsync("aicache:abc")).Should().Be("cached-response");
    }

    [Fact]
    public async Task GetAsync_cache_miss_returns_null()
    {
        var service = NewService();
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        (await service.GetAsync("aicache:abc")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_redis_throws_returns_null_and_does_not_propagate()
    {
        var service = NewService();
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        (await service.GetAsync("aicache:abc")).Should().BeNull("a Redis fault must fail open as a miss");
    }

    // ── SetAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_success_writes_value_with_ttl()
    {
        var service = NewService();
        var ttl = TimeSpan.FromHours(24);
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await service.SetAsync("aicache:abc", "value", ttl);

        _db.Verify(d => d.StringSetAsync(
            (RedisKey)"aicache:abc", (RedisValue)"value", ttl,
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_redis_throws_does_not_propagate()
    {
        var service = NewService();
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        await service.Invoking(s => s.SetAsync("aicache:abc", "value", TimeSpan.FromHours(24)))
            .Should().NotThrowAsync("a cache-write fault must be swallowed, never break the AI call path");
    }
}
