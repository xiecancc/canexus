namespace Application.Contracts.Monitor;
/// <summary>缓存运行信息（GET /api/monitor/cache；只读快照，不含任何敏感配置与键内容）。</summary>
public class CacheMonitorResponse
{
    /// <summary>是否启用 Redis 分布式缓存（false 表示双层缓存降级为进程内存形态）。</summary>
    public bool RedisEnabled { get; set; }

    /// <summary>Redis 是否连接成功（仅在 RedisEnabled=true 时有意义；连接失败时监控降级输出配置快照）。</summary>
    public bool? RedisConnected { get; set; }

    /// <summary>缓存键前缀（InstanceName，区分多实例共用同一 Redis 的键空间）。</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>Redis 服务器版本（未启用 / 未连接时为空）。</summary>
    public string? RedisVersion { get; set; }

    /// <summary>Redis 已运行天数（未启用 / 未连接时为 null）。</summary>
    public long? UptimeDays { get; set; }

    /// <summary>当前连接的客户端数（未启用 / 未连接时为 null）。</summary>
    public long? ConnectedClients { get; set; }

    /// <summary>已用内存（人类可读，如 "2.5M"；未启用 / 未连接时为空）。</summary>
    public string? UsedMemory { get; set; }

    /// <summary>最大内存限制（人类可读；0 或空表示未设置）。</summary>
    public string? MaxMemory { get; set; }

    /// <summary>逻辑库键总数（DBSIZE；未启用 / 未连接时为 null）。</summary>
    public long? TotalKeys { get; set; }

    /// <summary>键空间命中次数（自启动累计；未启用 / 未连接时为 null）。</summary>
    public long? KeyspaceHits { get; set; }

    /// <summary>键空间未命中次数（自启动累计；未启用 / 未连接时为 null）。</summary>
    public long? KeyspaceMisses { get; set; }

    /// <summary>缓存命中率（0~1，hits / (hits + misses)；无访问时分母为 0 返回 null）。</summary>
    public double? HitRate { get; set; }

    /// <summary>每秒命令执行数（瞬时值；未启用 / 未连接时为 null）。</summary>
    public long? OpsPerSec { get; set; }

    /// <summary>TTL 档位配置快照（本地 L1 / 分布式 L2 过期秒数，供前端展示缓存策略）。</summary>
    public CacheTtlSnapshotDto Ttl { get; set; } = new();
}

/// <summary>缓存 TTL 档位配置快照（秒）。</summary>
public class CacheTtlSnapshotDto
{
    /// <summary>热数据档（本地 L1 过期秒数 / 分布式 L2 过期秒数）。</summary>
    public TtlPairDto Hot { get; set; } = new();

    /// <summary>常规档。</summary>
    public TtlPairDto Medium { get; set; } = new();

    /// <summary>冷数据档。</summary>
    public TtlPairDto Cold { get; set; } = new();
}

/// <summary>TTL 档位对（本地 / 分布式过期秒数）。</summary>
public class TtlPairDto
{
    /// <summary>本地 L1 过期秒数。</summary>
    public int LocalSeconds { get; set; }

    /// <summary>分布式 L2 过期秒数。</summary>
    public int DistributedSeconds { get; set; }
}
