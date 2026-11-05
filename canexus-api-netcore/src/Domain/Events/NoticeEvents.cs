
using Domain.Entities;

namespace Domain.Events;
/// <summary>通知公告创建领域事件：公告创建成功后发布，Domain 为创建后的状态（消息推送等响应动作可由监听器扩展处理）。</summary>
/// <param name="newEntity">创建后的通知公告。</param>
public sealed class NoticeCreatedEvent(SysNotice newEntity) : AggregateCreatedEvent<SysNotice>(newEntity);

/// <summary>通知公告修改领域事件：公告更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。</summary>
/// <param name="oldEntity">变更前的公告快照。</param>
/// <param name="newEntity">变更后的公告。</param>
public sealed class NoticeUpdatedEvent(SysNotice oldEntity, SysNotice newEntity) : AggregateUpdatedEvent<SysNotice>(oldEntity, newEntity);

/// <summary>通知公告删除领域事件：公告删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的通知公告。</param>
public sealed class NoticeDeletedEvent(SysNotice newEntity) : AggregateDeletedEvent<SysNotice>(newEntity);

/// <summary>通知公告恢复领域事件：公告恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的通知公告。</param>
public sealed class NoticeRestoredEvent(SysNotice newEntity) : AggregateRestoredEvent<SysNotice>(newEntity);
