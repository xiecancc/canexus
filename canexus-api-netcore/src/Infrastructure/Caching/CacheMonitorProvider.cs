
using Domain.Services;
using Infrastructure.Redis;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Caching;
/// <summary>基于 Redis 的缓存运行信息提供者（监控端点消费）：
/// 按 <see cref="CacheOption"/> 的连接配置懒加载建连（经 <c>RedisConnectionOptions.Create</c> 统一超时 / 重试 / 容错参数，与 Cache / DistributedLock 同源），
/// 读取 INFO（memory / clients / stats / server）与 DBSIZE 组装概要。
/// <para>Redis 未启用（<see cref="CacheOption.RedisEnabled"/> = false）或连接失败时返回未连接降级快照，
/// 不抛出异常——监控端点应始终可响应，缓存故障由健康检查（CacheHealthCheck）承担告警职责；
/// 连接失败不缓存失败状态，后续监控请求会重新尝试建连（异步可重试降级）。</para></summary>
/// <remarks>构造函数。</remarks>
/// <param name="monitor">缓存配置监视器（读取 RedisEnabled / RedisConfiguration 最新值）。</param>
public sealed class CacheMonitorProvider(IOptionsMonitor<CacheOption> monitor) : ICacheMonitorProvider
{
    /// <summary>已建立的 Redis 连接（懒加载 + 复用；仅 RedisEnabled 时初始化）。</summary>
    private ConnectionMultiplexer? _connection;

    /// <summary>连接初始化锁（防止并发首访重复建连）。</summary>
    private readonly SemaphoreSlim _initGate = new(1, 1);

    /// <inheritdoc />
    public async Task<CacheRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var option = monitor.CurrentValue;
        if (!option.RedisEnabled)
        {
            return new CacheRuntimeSnapshot(Connected: false);
        }

        var connection = await GetOrConnectAsync(option, cancellationToken);
        if (connection is null || !connection.IsConnected)
        {
            return new CacheRuntimeSnapshot(Connected: false);
        }

        var server = connection.GetServers().FirstOrDefault(s => !s.IsReplica)
            ?? connection.GetServers().First();
        var info = (await server.InfoAsync())
            .SelectMany(group => group)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        long.TryParse(info.GetValueOrDefault("keyspace_hits"), out var hits);
        long.TryParse(info.GetValueOrDefault("keyspace_misses"), out var misses);
        long.TryParse(info.GetValueOrDefault("instantaneous_ops_per_sec"), out var opsPerSec);
        long.TryParse(info.GetValueOrDefault("uptime_in_days"), out var uptimeDays);
        long.TryParse(info.GetValueOrDefault("connected_clients"), out var clients);

        return new CacheRuntimeSnapshot(
            Connected: true,
            RedisVersion: info.GetValueOrDefault("redis_version"),
            UptimeDays: uptimeDays,
            ConnectedClients: clients,
            UsedMemory: info.GetValueOrDefault("used_memory_human"),
            MaxMemory: info.GetValueOrDefault("maxmemory_human") is { Length: > 0 } max ? max : null,
            TotalKeys: await server.DatabaseSizeAsync(connection.GetDatabase().Database),
            KeyspaceHits: hits,
            KeyspaceMisses: misses,
            OpsPerSec: opsPerSec);
    }

    /// <summary>
    /// 获取或建立 Redis 连接（懒加载 + 统一连接参数 + 异步建连 + 失败可重试）。
    /// <para>连接失败返回 null 并保持降级，但不缓存「已初始化 / 永久失败」状态：
    /// <c>_connection</c> 保持为 null，后续监控请求会重新尝试建连（异步可重试降级）。</para>
    /// </summary>
    /// <param name="option">缓存配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接多路复用器；失败为 null。</returns>
    private async Task<ConnectionMultiplexer?> GetOrConnectAsync(CacheOption option, CancellationToken cancellationToken)
    {
        if (_connection is { } established)
        {
            return established;
        }

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            // 双检：可能已被并发首访建立
            if (_connection is null)
            {
                try
                {
                    // 走统一构造入口：与缓存 L2 / 失效广播 / 分布式锁 / 分布式限流共用超时 / 重试 / 离线容错口径；
                    // ConnectAsync 为真正的异步建连，避免旧实现以 Task.Run 包裹同步 Connect 造成线程阻塞。
                    _connection = await ConnectionMultiplexer.ConnectAsync(
                        RedisConnectionOptions.Create(option.RedisConfiguration));
                }
                catch (Exception)
                {
                    // 监控端点须始终可响应：连接 / 参数解析失败一律降级；
                    // 不设置任何「已初始化」闩锁，_connection 保持 null，后续请求可重新尝试（可重试降级）。
                    return null;
                }
            }

            return _connection;
        }
        finally
        {
            _initGate.Release();
        }
    }
}
