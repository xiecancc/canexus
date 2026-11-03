
using Domain.Services;
using Quartz;
using Quartz.Impl.Matchers;

namespace Infrastructure.Scheduling;
/// <summary>基于 Quartz ISchedulerFactory 的调度器运行信息提供者（监控端点消费）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="schedulerFactory">Quartz 调度器工厂。</param>
public sealed class SchedulerInfoProvider(ISchedulerFactory schedulerFactory) : ISchedulerInfoProvider
{
    /// <inheritdoc />
    public async Task<SchedulerSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var runningJobs = await scheduler.GetCurrentlyExecutingJobs(cancellationToken);
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);
        var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken);

        return new SchedulerSummary(
            scheduler.IsStarted,
            scheduler.InStandbyMode,
            runningJobs.Count,
            jobKeys.Count,
            triggerKeys.Count);
    }
}
