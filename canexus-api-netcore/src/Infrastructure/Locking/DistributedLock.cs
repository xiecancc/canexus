
using Infrastructure.Redis;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Locking;
/// <summary>
/// 分布式锁实现：Redis 启用时经 SET NX PX + 令牌校验释放（原子安全，防止误删他人锁）；
/// 未启用（单机部署）时降级为进程内 SemaphoreSlim。
/// <para>与缓存层一致的容错策略：Redis 故障时不抛异常，返回 null（未获取到）交由调用方降级为无锁路径，
/// 正确性由限流 / TTL / 幂等判重兜底（见 <see cref="IDistributedLock"/> 文档）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="monitor">缓存配置监视器（Redis 开关 / 连接串 / 实例名前缀，与缓存键空间一致）。</param>
/// <param name="logger">日志器。</param>
public sealed class DistributedLock(IOptionsMonitor<CacheOption> monitor, ILogger<DistributedLock> logger) : IDistributedLock
{
    /// <summary>锁键前缀（复用缓存实例名，避免与其他系统的 Redis 键空间冲突；最终格式：{instance}lock:{key}）。</summary>
    private string Prefix => monitor.CurrentValue.InstanceName + "lock:";

    /// <summary>本地降级锁池条带数（P1-R8：固定容量条带锁——按键哈希取条带，容量恒定不随动态键增长；
    /// 代价是同条带的不同键互斥，作为"正确性增强"的降级路径可接受）。</summary>
    private const int LocalLockStripes = 64;

    /// <summary>进程内锁池（Redis 未启用时的降级实现）：固定长度信号量条带，无字典增长问题。</summary>
    private readonly SemaphoreSlim[] _localLocks =
        [.. Enumerable.Range(0, LocalLockStripes).Select(_ => new SemaphoreSlim(1, 1))];

    /// <summary>Redis 连接（懒加载单例：与缓存层各自持有连接，锁使用频率低，独立连接避免相互影响）。</summary>
    private ConnectionMultiplexer? _redis;

    /// <summary>Redis 连接初始化锁。</summary>
    private readonly SemaphoreSlim _redisInitGate = new(1, 1);

    /// <summary>锁释放脚本：持有令牌与当前值一致才删除（防止锁已过期被他人持有后被误删）。</summary>
    private const string ReleaseScript =
        """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!monitor.CurrentValue.RedisEnabled)
        {
            return await AcquireLocalAsync(key, waitTimeout, cancellationToken);
        }

        try
        {
            return await AcquireRedisAsync(key, expiry, waitTimeout, cancellationToken);
        }
        catch (Exception exception)
        {
            // 与缓存层一致的降级语义：锁后端故障不阻断业务，调用方按未获取到处理
            logger.LogWarning(exception, "分布式锁获取失败（Redis 异常），已降级为无锁路径：{LockKey}", key);
            return null;
        }
    }

    /// <summary>本地锁路径：进程内信号量条带（expiry 无意义——单进程内句柄必然确定性释放，进程崩溃锁即消失）。</summary>
    private async Task<IAsyncDisposable?> AcquireLocalAsync(string key, TimeSpan? waitTimeout, CancellationToken cancellationToken)
    {
        // 按键哈希取条带（无符号化避免负哈希），条带内互斥、条带间并行，容量恒定
        var stripe = (uint)string.GetHashCode(Prefix + key, StringComparison.Ordinal) % LocalLockStripes;
        var semaphore = _localLocks[stripe];
        var acquired = await semaphore.WaitAsync(waitTimeout ?? TimeSpan.Zero, cancellationToken);
        return acquired ? new LocalLockHandle(semaphore) : null;
    }

    /// <summary>Redis 锁路径：SET NX PX 原子抢占，未抢占到时按等待超时轮询重试。</summary>
    private async Task<IAsyncDisposable?> AcquireRedisAsync(string key, TimeSpan expiry, TimeSpan? waitTimeout, CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(cancellationToken);
        var fullKey = Prefix + key;
        var token = Guid.NewGuid().ToString("N");
        var deadline = waitTimeout is { } wait ? DateTime.UtcNow + wait : DateTime.UtcNow;

        do
        {
            if (await database.StringSetAsync(fullKey, token, expiry, When.NotExists))
            {
                return new RedisLockHandle(database, fullKey, token, logger);
            }

            await Task.Delay(50, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    /// <summary>获取 Redis 数据库（懒建连接，连接失败原样上抛由 AcquireAsync 统一降级）。</summary>
    private async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_redis is { } established)
        {
            return established.GetDatabase();
        }

        await _redisInitGate.WaitAsync(cancellationToken);
        try
        {
            if (_redis is null)
            {
                _redis = await ConnectionMultiplexer.ConnectAsync(
                    RedisConnectionOptions.Create(monitor.CurrentValue.RedisConfiguration));
            }
        }
        finally
        {
            _redisInitGate.Release();
        }

        return _redis.GetDatabase();
    }

    /// <summary>Redis 锁句柄：释放时经 Lua 脚本「令牌一致才删除」，原子且不误删他人锁。</summary>
    private sealed class RedisLockHandle(IDatabase database, string key, string token, ILogger logger) : IAsyncDisposable
    {
        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.ScriptEvaluateAsync(ReleaseScript, [new RedisKey(key)], [new RedisValue(token)]);
            }
            catch (Exception exception)
            {
                // 释放失败仅告警：锁有过期时间兜底，最迟在 expiry 后自然可被他人抢占
                logger.LogWarning(exception, "分布式锁释放失败（等待过期自动释放）：{LockKey}", key);
            }
        }
    }

    /// <summary>本地锁句柄：释放进程内信号量。</summary>
    private sealed class LocalLockHandle(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
