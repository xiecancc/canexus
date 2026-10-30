namespace Infrastructure.Caching;

using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

/// <summary>
/// 基于 HybridCache 的双层缓存实现：L1 进程内存（短过期）+ L2 Redis 分布式缓存（长过期）。
/// <para>读取顺序：L1 命中直接返回（零网络开销）→ L2 命中回填 L1 → 均未命中由调用方工厂回源并两级写入；
/// 写入同时落 L1 与 L2；失效同时清除两级。L1 / L2 过期时间按 <see cref="CacheTtl"/> 档位
/// （或 <see cref="CacheTtlProfile"/> 显式指定）从 <see cref="CacheOption"/> 读取，实现基于 TTL 的策略自动调整。
/// Redis 未启用或短暂不可用时自动退化为纯内存模式，不影响业务可用性。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">HybridCache 实例（单例）。</param>
/// <param name="monitor">缓存配置监视器（每次缓存操作读取最新值，TTL 档位支持热加载）。</param>
public sealed class Cache(HybridCache cache, IOptionsMonitor<CacheOption> monitor) : ICache
{
    /// <summary>当前缓存配置（经 IOptionsMonitor 读取，支持热加载）。</summary>
    private CacheOption Option => monitor.CurrentValue;
    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull
    {
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
        return await cache.GetOrCreateAsync<T?>(
            key,
            _ => ValueTask.FromResult(default(T)),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
    {
        await cache.SetAsync(key, value, BuildEntryOptions(ttl), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
    {
        await cache.SetAsync(key, value, BuildEntryOptions(profile), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await cache.RemoveAsync(key, cancellationToken);
    }

    /// <summary>按档位 / 显式 TTL 构造 HybridCache 条目选项：Expiration 为 L2 分布式过期，LocalCacheExpiration 为 L1 本地过期。</summary>
    /// <param name="ttl">TTL 档位。</param>
    /// <returns>HybridCache 条目选项。</returns>
    internal HybridCacheEntryOptions BuildEntryOptions(CacheTtl ttl) => ttl switch
    {
        CacheTtl.Hot => Pair(Option.Hot),
        CacheTtl.Cold => Pair(Option.Cold),
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
