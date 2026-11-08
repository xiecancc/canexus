
using Infrastructure.Redis;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Caching;
/// <summary>
/// 基于 HybridCache 的双层缓存实现：L1 进程内存（短过期）+ L2 Redis 分布式缓存（长过期）。
/// <para>读取顺序：L1 命中直接返回（零网络开销）→ L2 命中回填 L1 → 均未命中由调用方工厂回源并两级写入；
/// 写入同时落 L1 与 L2；失效同时清除两级。L1 / L2 过期时间按 <see cref="CacheTtl"/> 档位
/// （或 <see cref="CacheTtlProfile"/> 显式指定）从 <see cref="CacheOption"/> 读取，实现基于 TTL 的策略自动调整。</para>
/// <para>跨实例 L1 失效广播（P1-R7）：显式 <see cref="RemoveAsync"/> 时经 Redis Pub/Sub 广播缓存键，
/// 其他实例收到后清除本地 L1——消除"A 实例写库失效后，B 实例 L1 旧值滞留至本地 TTL 过期"的一致性窗口
///（多实例 + Redis 部署下生效；单机 / 未启用 Redis 时广播为空操作，一致性由 TTL 兜底）。
/// 回填类读取（GetOrCreate）不广播：其 L1 短过期 + L2 统一视图已保证收敛，广播只会放大开销。</para>
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

    /// <summary>失效广播频道名（带实例名前缀，避免与其他系统键空间冲突）。</summary>
    private string InvalidationChannel => Option.InstanceName + "cache:invalidate";

    /// <summary>Pub/Sub 订阅 / 发布连接（懒加载单例：仅多实例广播需要，与 L2 数据连接分离，故障互不影响）。</summary>
    private ConnectionMultiplexer? _broadcastRedis;

    /// <summary>广播连接初始化锁。</summary>
    private readonly SemaphoreSlim _broadcastInitGate = new(1, 1);

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
        //
        // flags 禁用两级写入是「只读」语义的关键：HybridCache 默认会把 factory 结果也写入缓存，
        // 对值类型而言就是缓存 false / 0（例如会话吊销状态），会把一次「未命中」固化成
        // TTL 内的确定性假值，后续显式 SetAsync 之前其他实例读到的都是旧假值。禁止写入后，
        // 「不存在」永远表示未命中，只有调用方显式 SetAsync 的值才会在 L1 / L2 中存在。
        try
        {
            return await cache.GetOrCreateAsync<T?>(
                key,
                _ => ValueTask.FromResult(default(T)),
                new HybridCacheEntryOptions
                {
                    Flags = HybridCacheEntryFlags.DisableLocalCacheWrite
                        | HybridCacheEntryFlags.DisableDistributedCacheWrite
                },
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

        // 跨实例广播：其他实例清除各自 L1（本实例无需自广播；订阅回调走 RemoveLocalAsync 不会回环）
        if (Option.RedisEnabled)
        {
            await BroadcastInvalidationAsync(key, cancellationToken);
        }
    }

    /// <summary>广播缓存键失效消息至其他实例（发布失败仅告警，不阻断业务；订阅侧各自清本地 L1）。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task BroadcastInvalidationAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexer = await GetBroadcastRedisAsync(cancellationToken);
            if (multiplexer is null)
            {
                return;
            }

            await multiplexer.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(InvalidationChannel), new RedisValue(key));
        }
        catch (Exception exception)
        {
            // 广播失败仅告警：其他实例 L1 旧值最迟在本地 TTL 过期后自然失配
            logger.LogWarning(exception, "缓存失效广播失败，其他实例依赖 TTL 兜底：{CacheKey}", key);
        }
    }

    /// <summary>
    /// 获取广播连接（懒建连接 + 幂等订阅）：连接失败返回 null（降级为无广播，TTL 兜底）；
    /// 首次成功连接时订阅失效频道，收到消息即清除本地两级缓存（经 HybridCache 直清，不回环广播）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>广播连接；不可用时为 null。</returns>
    private async Task<ConnectionMultiplexer?> GetBroadcastRedisAsync(CancellationToken cancellationToken)
    {
        if (_broadcastRedis is { } established)
        {
            return established;
        }

        await _broadcastInitGate.WaitAsync(cancellationToken);
        try
        {
            if (_broadcastRedis is null)
            {
                var multiplexer = await ConnectionMultiplexer.ConnectAsync(
                    RedisConnectionOptions.Create(Option.RedisConfiguration));
                var subscriber = multiplexer.GetSubscriber();
                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(InvalidationChannel),
                    (channel, message) =>
                    {
                        // 订阅回调：清除本地两级缓存（不再次广播，避免回环）；失败静默（TTL 兜底）
                        _ = cache.RemoveAsync(message.ToString());
                    });
                _broadcastRedis = multiplexer;
            }
        }
        catch (Exception exception)
        {
            // 广播为一致性增强而非正确性依赖：连接 / 订阅失败降级为无广播
            logger.LogWarning(exception, "缓存失效广播连接建立失败，本实例降级为无广播模式（TTL 兜底）");
            return null;
        }
        finally
        {
            _broadcastInitGate.Release();
        }

        return _broadcastRedis;
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
