namespace Domain.Events;

using Domain.Entities;

/// <summary>定时任务创建领域事件：任务创建成功后发布，携带创建后的状态（调度同步仍由处理器经 IJobScheduleManager 完成，本事件面向后续监听扩展）。</summary>
/// <param name="Entity">创建后的定时任务。</param>
public sealed class JobCreatedEvent(SysJob entity) : CreatedEvent<SysJob>("job.created", entity);

/// <summary>定时任务修改领域事件：任务更新（含启停与调度同步）成功后发布，携带变更前后状态。</summary>
/// <param name="Before">变更前的任务快照。</param>
/// <param name="After">变更后的任务。</param>
public sealed class JobUpdatedEvent(SysJob before, SysJob after) : UpdatedEvent<SysJob>("job.updated", before, after);

/// <summary>定时任务删除领域事件：任务删除成功后发布，携带删除前的状态。</summary>
/// <param name="Entity">删除前的定时任务。</param>
public sealed class JobDeletedEvent(SysJob entity) : DeletedEvent<SysJob>("job.deleted", entity);
