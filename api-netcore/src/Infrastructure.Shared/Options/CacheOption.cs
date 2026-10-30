namespace Infrastructure.Shared.Options;

/// <summary>
/// 双层缓存配置（appsettings.json 的 "Cache" 节）：
/// <para>RedisEnabled=false 时仅使用进程内存（L1），开发 / 测试环境无需 Redis 即可运行；
/// 生产环境开启后自动注册 StackExchange.Redis 作为 L2 分布式缓存。</para>
/// </summary>
public sealed class CacheOption : OptionBase
{
    /// <summary>是否启用 Redis 作为 L2 分布式缓存；false 时退化为纯进程内存缓存。</summary>
    public bool RedisEnabled { get; set; }

    /// <summary>Redis 连接串（StackExchange.Redis 格式）。</summary>
    public string RedisConfiguration { get; set; } = "localhost:6379";

    /// <summary>Redis 键前缀（多应用共享 Redis 时隔离命名空间）。</summary>
    public string InstanceName { get; set; } = "super-admin:";

    /// <summary>单条缓存负载上限（字节），超出则不写入二级缓存，防止大对象拖垮序列化。</summary>
    public long MaximumPayloadBytes { get; set; } = 1024 * 1024;

    /// <summary>热点档：L1 / L2 过期秒数（默认 30 / 300）。</summary>
    public TtlPair Hot { get; set; } = new() { LocalSeconds = 30, DistributedSeconds = 300 };

    /// <summary>常规档：L1 / L2 过期秒数（默认 60 / 600）。</summary>
    public TtlPair Medium { get; set; } = new() { LocalSeconds = 60, DistributedSeconds = 600 };

    /// <summary>冷数据档：L1 / L2 过期秒数（默认 300 / 1800）。</summary>
    public TtlPair Cold { get; set; } = new() { LocalSeconds = 300, DistributedSeconds = 1800 };

    /// <inheritdoc />
    public override void Validate()
    {
        Ensure(MaximumPayloadBytes > 0, nameof(MaximumPayloadBytes), "单条缓存负载上限必须大于 0");
        ValidateTtlPair(Hot, nameof(Hot));
        ValidateTtlPair(Medium, nameof(Medium));
        ValidateTtlPair(Cold, nameof(Cold));

        // Redis 相关配置仅在启用 L2 分布式缓存时强制校验（未启用时不参与运行）
        if (RedisEnabled)
        {
            EnsureNotNullOrWhiteSpace(RedisConfiguration, nameof(RedisConfiguration), "启用 Redis 时 Cache:RedisConfiguration 不能为空");
        }

        EnsureNotNull(InstanceName, nameof(InstanceName));
    }

    /// <summary>校验单个 TTL 档位：秒数须在 1-86400（一天）之间，且 L1 本地过期不得长于 L2 分布式过期。</summary>
    /// <param name="pair">档位配置。</param>
    /// <param name="name">档位属性名（Hot / Medium / Cold，用于错误消息定位）。</param>
    private void ValidateTtlPair(TtlPair pair, string name)
    {
        EnsureInRange(pair.LocalSeconds, 1, 86_400, $"{name}.{nameof(TtlPair.LocalSeconds)}",
            $"{name}.LocalSeconds 必须在 1-86400 秒之间");
        EnsureInRange(pair.DistributedSeconds, 1, 86_400, $"{name}.{nameof(TtlPair.DistributedSeconds)}",
            $"{name}.DistributedSeconds 必须在 1-86400 秒之间");
        Ensure(pair.LocalSeconds <= pair.DistributedSeconds, name,
            $"{name} 档位的 L1 本地过期（{pair.LocalSeconds}s）不应长于 L2 分布式过期（{pair.DistributedSeconds}s）");
    }

    /// <summary>单档位的两级过期时间（秒）。</summary>
    public sealed class TtlPair
    {
        /// <summary>L1 进程内存过期秒数（短，保证一致性延迟上限）。</summary>
        public int LocalSeconds { get; set; }

        /// <summary>L2 Redis 过期秒数（长，保证跨实例 / 重启后的命中率）。</summary>
        public int DistributedSeconds { get; set; }
    }
}
