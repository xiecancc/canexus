
using System.Diagnostics;
using Application.Abstractions;
using Application.Contracts.Monitor;
using Application.Contracts.Queries;
using Domain.Services;

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
