
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Caching;
/// <summary>
/// 基于 HybridCache 的双层缓存实现：L1 进程内存（短过期）+ L2 Redis 分布式缓存（长过期）。
/// <para>读取顺序：L1 命中直接返回（零网络开销）→ L2 命中回填 L1 → 均未命中由调用方工厂回源并两级写入；
/// 写入同时落 L1 与 L2；失效同时清除两级。L1 / L2 过期时间按 <see cref="CacheTtl"/> 档位
/// （或 <see cref="CacheTtlProfile"/> 显式指定）从 <see cref="CacheOption"/> 读取，实现基于 TTL 的策略自动调整。</para>
/// <para>统一容错策略（本类为全应用缓存操作的唯一出口，所有使用方一次受益）：
/// 单条 Get / Set / Remove 失败（如 Redis 短暂不可用）不向上抛异常，降级为"未命中 / 忽略写入"
/// 并以 Warning 记录，业务可用性优先，数据一致性由 TTL 过期兜底最终一致；
/// <see cref="GetOrCreateAsync{T}(string, Func{CancellationToken, ValueTask{T}}, CacheTtl, CancellationToken)"/>
/// 的 factory 回源属于业务路径，其业务异常原样上抛、本类不做包裹（包裹后 catch 中重入 factory
/// 会导致回源委托二次执行的副作用风险）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">HybridCache 实例（单例）。</param>
/// <param name="monitor">缓存配置监视器（每次缓存操作读取最新值，TTL 档位支持热加载）。</param>
/// <param name="logger">日志器。</param>
public sealed class Cache(HybridCache cache, IOptionsMonitor<CacheOption> monitor, ILogger<Cache> logger) : ICache
{
    /// <summary>当前缓存配置（经 IOptionsMonitor 读取，支持热加载）。</summary>
    private CacheOption Option => monitor.CurrentValue;

    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull
    {
        // factory 为业务回源路径：业务异常原样上抛（由全局异常中间件统一处理）；
        // 缓存基础设施故障的容错由 HybridCache 内部承担，本方法不做包裹
        return await cache.GetOrCreateAsync(
            key,
            async token => await factory(token),
            BuildEntryOptions(ttl),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // 说明：HybridCache 10.x 的 GetValueOrDefault 仅查询 L2 分布式缓存、不读 L1，
        // 因此此处用 GetOrCreateAsync 包装（factory 恒返回 default 以实现"只读不回源"语义，
        // L1 → L2 逐级读取，均未命中时返回 default(T)）。
        try
        {
            return await cache.GetOrCreateAsync<T?>(
                key,
                _ => ValueTask.FromResult(default(T)),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            // 统一容错：读取失败视为未命中，由调用方回源（数据库查询），不阻断业务
            logger.LogWarning(exception, "缓存读取失败，已降级为未命中：{CacheKey}", key);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetAsync(key, value, BuildEntryOptions(ttl), cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            // 统一容错：写入失败仅告警，缓存一致性由 TTL 过期兜底（下次过期后重新回源）
            logger.LogWarning(exception, "缓存写入失败，依赖 TTL 过期兜底最终一致：{CacheKey}", key);
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetAsync(key, value, BuildEntryOptions(profile), cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "缓存写入失败，依赖 TTL 过期兜底最终一致：{CacheKey}", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            // 统一容错：失效失败仅告警（如 Redis 抖动），旧值最迟在 TTL 过期后自然失配
            logger.LogWarning(exception, "缓存失效失败，依赖 TTL 过期兜底最终一致：{CacheKey}", key);
        }
    }

    /// <summary>按档位 / 显式 TTL 构造 HybridCache 条目选项：Expiration 为 L2 分布式过期，LocalCacheExpiration 为 L1 本地过期。</summary>
    /// <param name="ttl">TTL 档位。</param>
    /// <returns>HybridCache 条目选项。</returns>
    internal HybridCacheEntryOptions BuildEntryOptions(CacheTtl ttl) => ttl switch
    {
        CacheTtl.Hot => Pair(Option.Hot),
        CacheTtl.Cold => Pair(Option.Cold),

        // 不过期档：条目选项不设置过期时间（HybridCache 对 null 过期时间按永不过期处理），
        // 用于必须持久存续的元数据（如权限全局版本号），不参与档位配置
        CacheTtl.Never => new HybridCacheEntryOptions
        {
            Expiration = null,
            LocalCacheExpiration = null,
        },
        _ => Pair(Option.Medium),
    };

    /// <summary>按显式 TTL 构造条目选项（两级过期时间由调用方直接指定）。</summary>
    /// <param name="profile">显式 TTL。</param>
    /// <returns>HybridCache 条目选项。</returns>
    internal HybridCacheEntryOptions BuildEntryOptions(CacheTtlProfile profile) => new()
    {
        Expiration = profile.Distributed,
        LocalCacheExpiration = profile.Local,
    };

    /// <summary>档位对 → 条目选项。</summary>
    /// <param name="pair">两级过期秒数。</param>
    /// <returns>HybridCache 条目选项。</returns>
    private static HybridCacheEntryOptions Pair(CacheOption.TtlPair pair) => new()
    {
        Expiration = TimeSpan.FromSeconds(pair.DistributedSeconds),
        LocalCacheExpiration = TimeSpan.FromSeconds(pair.LocalSeconds),
    };
}
