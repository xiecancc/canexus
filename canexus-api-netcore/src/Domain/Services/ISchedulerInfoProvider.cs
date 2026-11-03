namespace Domain.Services;
/// <summary>调度器运行信息提供者：聚合 Quartz 调度器的运行状态概要（监控端点消费）。</summary>
public interface ISchedulerInfoProvider
{
    /// <summary>读取调度器运行概要（调度器不可用时抛出异常，由全局异常处理统一兜底）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>调度器概要。</returns>
    public Task<SchedulerSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>调度器运行概要。</summary>
/// <param name="IsStarted">调度器是否已启动。</param>
/// <param name="InStandbyMode">调度器是否处于待机（未启动任何触发器）。</param>
/// <param name="RunningJobCount">正在执行的作业数。</param>
/// <param name="TotalJobCount">调度器中登记的作业总数（含暂停）。</param>
/// <param name="TriggerCount">调度器中登记的触发器总数。</param>
public sealed record SchedulerSummary(
    bool IsStarted,
    bool InStandbyMode,
    int RunningJobCount,
    int TotalJobCount,
    int TriggerCount);
