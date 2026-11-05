
using Domain.Services;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Caching;
/// <summary>基于 Redis 的缓存运行信息提供者（监控端点消费）：
/// 按 <see cref="CacheOption"/> 的连接配置懒加载建连（与 Cache / DistributedLock 同源），
/// 读取 INFO（memory / clients / stats / server）与 DBSIZE 组装概要。
/// <para>Redis 未启用（<see cref="CacheOption.RedisEnabled"/> = false）或连接失败时返回未连接降级快照，
/// 不抛出异常——监控端点应始终可响应，缓存故障由健康检查（CacheHealthCheck）承担告警职责。</para></summary>
/// <remarks>构造函数。</remarks>
/// <param name="monitor">缓存配置监视器（读取 RedisEnabled / RedisConfiguration 最新值）。</param>
public sealed class CacheMonitorProvider(IOptionsMonitor<CacheOption> monitor) : ICacheMonitorProvider
{
    /// <summary>懒加载连接（首次监控请求建立并复用；仅 RedisEnabled 时初始化）。</summary>
    private Lazy<ConnectionMultiplexer?>? _lazyConnection;

    /// <summary>连接初始化锁（防止并发首访重复建连）。</summary>
    private readonly SemaphoreSlim _initGate = new(1, 1);

    /// <summary>是否已尝试初始化连接（失败后不再重试，重启或配置热更前保持降级）。</summary>
    private bool _initialized;

    /// <inheritdoc />
    public async Task<CacheRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var option = monitor.CurrentValue;
        if (!option.RedisEnabled)
        {
            return new CacheRuntimeSnapshot(Connected: false);
        }

        var connection = await GetOrConnectAsync(option);
        if (connection is null || !connection.IsConnected)
        {
            return new CacheRuntimeSnapshot(Connected: false);
        }

        var server = connection.GetServers().FirstOrDefault(s => !s.IsReplica)
            ?? connection.GetServers().First();
        var info = server.Info()
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

    /// <summary>获取或建立 Redis 连接（懒加载 + 幂等；连接失败返回 null 并保持降级）。</summary>
    /// <param name="option">缓存配置。</param>
    /// <returns>连接多路复用器；失败为 null。</returns>
    private async Task<ConnectionMultiplexer?> GetOrConnectAsync(CacheOption option)
    {
        if (_initialized && _lazyConnection?.Value is { } existing)
        {
            return existing;
        }

        await _initGate.WaitAsync();
        try
        {
            if (!_initialized)
            {
                _lazyConnection = new Lazy<ConnectionMultiplexer?>(() =>
                {
                    try
                    {
                        return ConnectionMultiplexer.Connect(option.RedisConfiguration);
                    }
                    catch (RedisConnectionException)
                    {
                        return null;
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication);
                _initialized = true;
            }

            var lazy = _lazyConnection;
            return lazy is null ? null : await Task.Run(() => lazy.Value);
        }
        finally
        {
            _initGate.Release();
        }
    }
}
