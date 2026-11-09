
using System.Threading.RateLimiting;
using API.RateLimiting;
using Infrastructure.Redis;
using StackExchange.Redis;

namespace API.Tests;

/// <summary>
/// Redis 韧性回归：连接参数统一启用超时 / 重试 / 离线容错；
/// Redis 停机时分布式限流 fail-open 放行，避免把业务请求变成 5xx。
/// </summary>
[TestFixture]
public class RedisResilienceTests
{
    /// <summary>
    /// 连接参数须带超时、重试与 AbortOnConnectFail=false（Redis 未就绪不阻断启动）。
    /// <para>超时取 StackExchange.Redis 默认 5000ms 而非自行缩短：高负载下的单次慢命令不应被误判为
    /// 「后端不可用」而触发限流 fail-open（曾导致全量并行测试中偶发 200 替代 429）。</para>
    /// </summary>
    [Test]
    public void ConnectionOptions_ApplyTimeoutRetryAndOfflineTolerance()
    {
        var options = RedisConnectionOptions.Create("localhost:6379,connectTimeout=100");

        Assert.Multiple(() =>
        {
            Assert.That(options.ConnectTimeout, Is.EqualTo(5000));
            Assert.That(options.SyncTimeout, Is.EqualTo(5000));
            Assert.That(options.ConnectRetry, Is.EqualTo(3));
            Assert.That(options.AbortOnConnectFail, Is.False, "Redis 离线时不得因连接失败阻断应用启动");
            Assert.That(options.ReconnectRetryPolicy, Is.Not.Null);
        });
    }

    /// <summary>Redis 后端抛异常时异步限流路径降级放行，租约仍为「已获取」。</summary>
    [Test]
    public async Task RateLimiter_RedisUnavailable_FailOpenOnAsyncPath()
    {
        using var limiter = new RedisFixedWindowRateLimiter(CreateFailingOptions());

        var lease = await limiter.AcquireAsync(1);

        Assert.That(lease.IsAcquired, Is.True,
            "限流后端故障不应把业务请求变成 5xx，应 fail-open 放行并告警");
    }

    /// <summary>同步限流路径同样降级放行。</summary>
    [Test]
    public void RateLimiter_RedisUnavailable_FailOpenOnSyncPath()
    {
        using var limiter = new RedisFixedWindowRateLimiter(CreateFailingOptions());

        var lease = limiter.AttemptAcquire(1);

        Assert.That(lease.IsAcquired, Is.True);
    }

    /// <summary>非法许可数属于编程错误，不得被降级逻辑吞掉。</summary>
    [Test]
    public void RateLimiter_InvalidPermitCount_StillThrows()
    {
        using var limiter = new RedisFixedWindowRateLimiter(CreateFailingOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.AttemptAcquire(0));
    }

    /// <summary>构造后端始终不可用的限流器参数。</summary>
    /// <returns>限流器参数。</returns>
    private static RedisFixedWindowRateLimiterOptions CreateFailingOptions() => new()
    {
        PermitLimit = 5,
        Window = TimeSpan.FromSeconds(60),
        ConnectionMultiplexerFactory = () => throw new RedisConnectionException(
            ConnectionFailureType.UnableToConnect, CommandFlags.None, "模拟 Redis 停机")
    };
}
