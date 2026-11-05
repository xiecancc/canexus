
using System.Diagnostics;
using Application.Abstractions;
using Application.Contracts.Monitor;
using Application.Contracts.Queries;
using Domain.Services;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;

namespace Application.Queries;
/// <summary>服务器运行信息查询处理器：进程 / GC / 运行时快照 + 调度器概要聚合（只读，不含敏感配置）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="schedulerInfoProvider">调度器运行信息提供者。</param>
public sealed class ServerMonitorQueryHandler(ISchedulerInfoProvider schedulerInfoProvider)
    : QueryHandler<ServerMonitorQuery, ServerMonitorResponse>
{
    /// <inheritdoc />
    protected override async Task<ServerMonitorResponse> HandleAsync(ServerMonitorQuery query, CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        var response = new ServerMonitorResponse
        {
            MachineName = Environment.MachineName,
            OsDescription = Environment.OSVersion.ToString(),
            OsArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            FrameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production",
            ProcessorCount = Environment.ProcessorCount,
            UptimeSeconds = Math.Max(0, (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds),
            WorkingSetMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            GcHeapMb = Math.Round(gcInfo.HeapSizeBytes / 1024d / 1024d, 1),
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            ServerTimeUtc = DateTime.UtcNow,
            Scheduler = new SchedulerSummaryDto()
        };

        var scheduler = await schedulerInfoProvider.GetSummaryAsync(cancellationToken);
        response.Scheduler = new SchedulerSummaryDto
        {
            IsStarted = scheduler.IsStarted,
            InStandbyMode = scheduler.InStandbyMode,
            RunningJobCount = scheduler.RunningJobCount,
            TotalJobCount = scheduler.TotalJobCount,
            TriggerCount = scheduler.TriggerCount
        };

        return response;
    }
}

/// <summary>缓存运行信息查询处理器：Redis 概要（经 <see cref="ICacheMonitorProvider"/> 端口）+ TTL 档位配置快照（只读，不含敏感配置与键内容）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="cacheMonitorProvider">缓存运行信息提供者。</param>
/// <param name="cacheMonitor">缓存配置监视器（读取 TTL 档位与 InstanceName 最新值）。</param>
public sealed class CacheMonitorQueryHandler(
    ICacheMonitorProvider cacheMonitorProvider,
    IOptionsMonitor<CacheOption> cacheMonitor)
    : QueryHandler<CacheMonitorQuery, CacheMonitorResponse>
{
    /// <inheritdoc />
    protected override async Task<CacheMonitorResponse> HandleAsync(CacheMonitorQuery query, CancellationToken cancellationToken)
    {
        var option = cacheMonitor.CurrentValue;
        var snapshot = await cacheMonitorProvider.GetSnapshotAsync(cancellationToken);

        var response = new CacheMonitorResponse
        {
            RedisEnabled = option.RedisEnabled,
            RedisConnected = option.RedisEnabled ? snapshot.Connected : null,
            InstanceName = option.InstanceName,
            RedisVersion = snapshot.RedisVersion,
            UptimeDays = snapshot.UptimeDays,
            ConnectedClients = snapshot.ConnectedClients,
            UsedMemory = snapshot.UsedMemory,
            MaxMemory = snapshot.MaxMemory,
            TotalKeys = snapshot.TotalKeys,
            KeyspaceHits = snapshot.KeyspaceHits,
            KeyspaceMisses = snapshot.KeyspaceMisses,
            HitRate = snapshot.KeyspaceHits is { } hits && snapshot.KeyspaceMisses is { } misses && hits + misses > 0
                ? Math.Round(hits / (double)(hits + misses), 4)
                : null,
            OpsPerSec = snapshot.OpsPerSec,
            Ttl = new CacheTtlSnapshotDto
            {
                Hot = ToTtlDto(option.Hot),
                Medium = ToTtlDto(option.Medium),
                Cold = ToTtlDto(option.Cold)
            }
        };

        return response;
    }

    /// <summary>TTL 档位对转响应 DTO。</summary>
    private static TtlPairDto ToTtlDto(CacheOption.TtlPair pair) => new()
    {
        LocalSeconds = pair.LocalSeconds,
        DistributedSeconds = pair.DistributedSeconds
    };
}
