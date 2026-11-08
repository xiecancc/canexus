using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace API.RateLimiting;

/// <summary>
/// Redis 固定窗口限流器参数。
/// <para>窗口起点以 Redis 服务端时间为准（TIME 命令），多副本对同一分区键读写同一 Redis 键，
/// 实现跨实例一致的固定窗口限流语义（P1-A5）。</para>
/// </summary>
public sealed class RedisFixedWindowRateLimiterOptions
{
    /// <summary>每分区每窗口允许请求数。</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>窗口时长。</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Redis 键前缀（多应用 / 多策略共享 Redis 时隔离命名空间）。</summary>
    public string KeyPrefix { get; set; } = "super-admin:rl:";

    /// <summary>策略标识（参与 Redis 键命名，避免不同限流策略互相污染）。</summary>
    public string PolicyId { get; set; } = "default";

    /// <summary>分区键（客户端 IP 等；参与 Redis 键命名，实现按分区独立计数）。</summary>
    public string PartitionKey { get; set; } = "unknown";

    /// <summary>ConnectionMultiplexer 工厂（由依赖注入提供，连接复用、不重复创建）。</summary>
    public Func<IConnectionMultiplexer> ConnectionMultiplexerFactory { get; set; } = null!;

    /// <summary>日志器（可选）：Redis 后端故障降级放行时记录告警，便于发现限流失效窗口。</summary>
    public ILogger? Logger { get; set; }
}
