namespace Infrastructure.Shared.Caching;

/// <summary>
/// 缓存 TTL 档位：按数据的过期特性选择策略，实现"基于过期时间自动调整缓存策略"。
/// <para>每档位对应一组 L1（进程内存）/ L2（Redis）过期时间，默认值可经配置文件覆盖：</para>
/// <list type="bullet">
/// <item><see cref="Hot"/>：高频短命数据（验证码）——L1 30s / L2 5min；</item>
/// <item><see cref="Medium"/>：常规只读数据（参数配置、字典）——L1 1min / L2 10min；</item>
/// <item><see cref="Cold"/>：低频稳定数据（权限集合、锁定状态）——L1 5min / L2 30min；</item>
/// <item><see cref="Never"/>：必须长期存续的元数据（如权限全局版本号）——L1 / L2 均不设置过期时间。</item>
/// </list>
/// <para>L1 短过期保证本实例内读性能与有限的一致性延迟；L2 长过期保证跨实例 / 重启后的命中率。
/// 需要精确控制时可改用 <see cref="CacheTtlProfile"/> 显式指定两级过期时间。</para>
/// </summary>
public enum CacheTtl
{
    /// <summary>热点档：L1 30 秒 / L2 5 分钟（高频、允许秒级延迟一致）。</summary>
    Hot,

    /// <summary>常规档：L1 1 分钟 / L2 10 分钟（默认档）。</summary>
    Medium,

    /// <summary>冷数据档：L1 5 分钟 / L2 30 分钟（稳定、读多写少）。</summary>
    Cold,

    /// <summary>不过期档：L1 / L2 均不设置过期时间，条目长期驻留（仅经显式失效或容量淘汰移除）。
    /// 适用于过期会破坏正确性的元数据——如权限全局版本号：过期回退 0 会导致旧版本缓存 key 复活。
    /// 本档位不参与 <see cref="CacheOption"/> 档位配置，固定为不过期。</summary>
    Never,
}

/// <summary>显式 TTL 档位：需要精确控制两级过期时间时使用（如验证码 5 分钟、登录锁定 10 分钟）。</summary>
/// <param name="Local">L1 进程内存过期时间（建议显著短于 L2）。</param>
/// <param name="Distributed">L2 分布式（Redis）过期时间。</param>
public readonly record struct CacheTtlProfile(TimeSpan Local, TimeSpan Distributed)
{
    /// <summary>L1 / L2 使用同一过期时间（适合一次性数据，如验证码）。</summary>
    /// <param name="ttl">统一过期时间。</param>
    /// <returns>TTL 档位。</returns>
    public static CacheTtlProfile Uniform(TimeSpan ttl) => new(ttl, ttl);
}
