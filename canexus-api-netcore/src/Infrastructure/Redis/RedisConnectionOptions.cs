
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Redis 连接参数统一构造入口（利用 StackExchange.Redis 原生 <see cref="ConfigurationOptions"/>）：
/// 缓存 L2、失效广播、分布式锁、分布式限流四处共用同一超时 / 重试 / 容错口径，
/// 避免各组件按字符串裸连导致「Redis 停机时同步阻塞启动或长时间挂起」。
/// </summary>
public static class RedisConnectionOptions
{
    /// <summary>
    /// 连接 / 同步操作超时（毫秒）：与 StackExchange.Redis 默认值（5000ms）保持一致。
    /// <para>不自行缩短库默认超时：全量测试 / 生产高峰下偶发的单次命令变慢若被判为「后端不可用」，
    /// 会让限流 fail-open 静默放行、缓存 L2 回源放大，等于把瞬时抖动放大成保护性设施失效。
    /// 真正需要快速失败的场景应由各层的 CancellationToken / 熔断策略表达，而非全局缩短库默认值。</para>
    /// </summary>
    private const int TimeoutMilliseconds = 5000;

    /// <summary>由连接串构造带超时与容错策略的连接参数。</summary>
    /// <param name="connectionString">StackExchange.Redis 格式连接串。</param>
    /// <returns>连接参数。</returns>
    public static ConfigurationOptions Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = ConfigurationOptions.Parse(connectionString);
        options.ConnectTimeout = TimeoutMilliseconds;
        options.SyncTimeout = TimeoutMilliseconds;
        options.ConnectRetry = 3;
        options.ReconnectRetryPolicy = new ExponentialRetry(100);
        options.KeepAlive = 60;
        // Redis 未就绪时不阻断应用启动 / 不抛连接异常：各层已有降级路径
        //（缓存 TTL 兜底、锁降级无锁、限流 fail-open、HybridCache 回源）
        options.AbortOnConnectFail = false;
        return options;
    }
}
