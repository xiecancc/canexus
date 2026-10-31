namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>定时任务变更领域事件：任务创建 / 更新（含启停与调度同步）/ 删除成功后发布。调度同步仍由处理器经 IJobScheduleManager 完成，本事件面向后续监听扩展。</summary>
/// <param name="JobId">变更的定时任务 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class JobChangedEvent(Guid jobId, AggregateChangeType changeType)
    : AggregateChangedEvent("job.changed", jobId, changeType)
{
    /// <summary>变更的定时任务 Id。</summary>
    public Guid JobId { get; } = jobId;
}
