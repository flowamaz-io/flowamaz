using FluentAssertions;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Flowamaz.Tests.Integration.Infrastructure;

/// <summary>
/// Shares a single real Redis 7 container across all rate-limit facts. Each test uses a unique
/// key so the shared instance can't cause cross-test interference.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ConnectionMultiplexer? _multiplexer;
    public IConnectionMultiplexer Multiplexer => _multiplexer!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }
        await _container.DisposeAsync().AsTask();
    }
}

/// <summary>
/// Exercises <see cref="RateLimitService"/> against real Redis: the atomic INCR+EXPIRE Lua
/// script, the allow/deny boundary at maxCount, and TTL-driven window expiry.
/// </summary>
public class RateLimitServiceTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;
    private readonly RateLimitService _service;

    public RateLimitServiceTests(RedisFixture fixture)
    {
        _fixture = fixture;
        _service = new RateLimitService(_fixture.Multiplexer, NullLogger<RateLimitService>.Instance);
    }

    private IDatabase Db => _fixture.Multiplexer.GetDatabase();
    private static string NewKey() => $"user:{Guid.NewGuid():N}";

    [Fact]
    public async Task First_call_increments_to_one_and_sets_ttl()
    {
        var key = NewKey();

        var allowed = await _service.CheckAndIncrementAsync(key, maxCount: 5, windowSeconds: 60);

        allowed.Should().BeTrue();

        var redisKey = $"ratelimit:{key}";
        ((long)await Db.StringGetAsync(redisKey)).Should().Be(1);

        var ttl = await Db.KeyTimeToLiveAsync(redisKey);
        ttl.Should().NotBeNull("the freshly-created counter must carry a TTL");
        ttl!.Value.TotalSeconds.Should().BeGreaterThan(0);
        ttl.Value.TotalSeconds.Should().BeLessOrEqualTo(60);
    }

    [Fact]
    public async Task Calls_up_to_max_are_allowed_then_next_is_denied()
    {
        var key = NewKey();
        const int max = 3;

        for (var call = 1; call <= max; call++)
        {
            (await _service.CheckAndIncrementAsync(key, max, windowSeconds: 60))
                .Should().BeTrue($"call {call} is within the limit of {max}");
        }

        (await _service.CheckAndIncrementAsync(key, max, windowSeconds: 60))
            .Should().BeFalse("the call at maxCount+1 must be denied");
    }

    [Fact]
    public async Task Key_expires_after_window_then_a_fresh_window_starts()
    {
        var key = NewKey();
        var redisKey = $"ratelimit:{key}";

        await _service.CheckAndIncrementAsync(key, maxCount: 2, windowSeconds: 1);
        (await Db.KeyTimeToLiveAsync(redisKey)).Should().NotBeNull();

        // Wait past the 1s window, then confirm Redis expired the counter (TTL gone, key absent).
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        (await Db.KeyExistsAsync(redisKey)).Should().BeFalse();
        (await Db.KeyTimeToLiveAsync(redisKey)).Should().BeNull();

        // The next call opens a brand new window at count 1.
        (await _service.CheckAndIncrementAsync(key, maxCount: 2, windowSeconds: 60))
            .Should().BeTrue();
        ((long)await Db.StringGetAsync(redisKey)).Should().Be(1);
    }
}
