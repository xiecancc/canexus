
using Domain.Entities;

namespace Domain.Events;
/// <summary>定时任务创建领域事件：任务创建成功后发布，Domain 为创建后的状态（调度同步由 Outbox 提交后事件监听器 JobScheduleSynchronizationHandler 完成，本事件面向后续监听扩展）。</summary>
/// <param name="newEntity">创建后的定时任务。</param>
public sealed class JobCreatedEvent(SysJob newEntity) : AggregateCreatedEvent<SysJob>(newEntity);

/// <summary>定时任务修改领域事件：任务更新（含启停）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照；调度同步由 Outbox 提交后事件监听器完成。</summary>
/// <param name="oldEntity">变更前的任务快照。</param>
/// <param name="newEntity">变更后的任务。</param>
public sealed class JobUpdatedEvent(SysJob oldEntity, SysJob newEntity) : AggregateUpdatedEvent<SysJob>(oldEntity, newEntity);

/// <summary>定时任务删除领域事件：任务删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的定时任务。</param>
public sealed class JobDeletedEvent(SysJob newEntity) : AggregateDeletedEvent<SysJob>(newEntity);

/// <summary>定时任务恢复领域事件：任务恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的定时任务。</param>
public sealed class JobRestoredEvent(SysJob newEntity) : AggregateRestoredEvent<SysJob>(newEntity);
