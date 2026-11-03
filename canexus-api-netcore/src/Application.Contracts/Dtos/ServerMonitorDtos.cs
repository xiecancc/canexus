namespace Application.Contracts.Monitor;
/// <summary>服务器与进程运行信息（GET /api/monitor/server；只读快照，不含任何敏感配置）。</summary>
public class ServerMonitorResponse
{
    /// <summary>主机名。</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>操作系统描述。</summary>
    public string OsDescription { get; set; } = string.Empty;

    /// <summary>操作系统架构（如 X64）。</summary>
    public string OsArchitecture { get; set; } = string.Empty;

    /// <summary>运行时框架描述（如 .NET 10.0.x）。</summary>
    public string FrameworkDescription { get; set; } = string.Empty;

    /// <summary>运行环境名（Development / Production 等）。</summary>
    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>CPU 逻辑核心数。</summary>
    public int ProcessorCount { get; set; }

    /// <summary>进程已运行时长（秒）。</summary>
    public double UptimeSeconds { get; set; }

    /// <summary>进程工作集内存（MB）。</summary>
    public double WorkingSetMb { get; set; }

    /// <summary>GC 托管堆大小（MB）。</summary>
    public double GcHeapMb { get; set; }

    /// <summary>进程线程数（约数，运行期动态变化）。</summary>
    public int ThreadCount { get; set; }

    /// <summary>进程句柄数。</summary>
    public int HandleCount { get; set; }

    /// <summary>服务器当前时间（UTC）。</summary>
    public DateTime ServerTimeUtc { get; set; }

    /// <summary>调度器概要。</summary>
    public SchedulerSummaryDto Scheduler { get; set; } = new();
}

/// <summary>调度器运行概要（Quartz）。</summary>
public class SchedulerSummaryDto
{
    /// <summary>调度器是否已启动。</summary>
    public bool IsStarted { get; set; }

    /// <summary>调度器是否处于待机模式。</summary>
    public bool InStandbyMode { get; set; }

    /// <summary>正在执行的作业数。</summary>
    public int RunningJobCount { get; set; }

    /// <summary>登记的作业总数（含暂停）。</summary>
    public int TotalJobCount { get; set; }

    /// <summary>登记的触发器总数。</summary>
    public int TriggerCount { get; set; }
}
