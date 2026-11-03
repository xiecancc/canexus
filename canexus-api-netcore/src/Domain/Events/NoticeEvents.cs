
using Domain.Entities;

namespace Domain.Events;
/// <summary>通知公告创建领域事件：公告创建成功后发布，Domain 为创建后的状态（消息推送等响应动作可由监听器扩展处理）。</summary>
/// <param name="entity">创建后的通知公告。</param>
public sealed class NoticeCreatedEvent(SysNotice entity) : AggregateCreatedEvent<SysNotice>(entity);

/// <summary>通知公告修改领域事件：公告更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。</summary>
/// <param name="before">变更前的公告快照。</param>
/// <param name="after">变更后的公告。</param>
public sealed class NoticeUpdatedEvent(SysNotice before, SysNotice after) : AggregateUpdatedEvent<SysNotice>(before, after);

/// <summary>通知公告删除领域事件：公告删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="entity">删除前的通知公告。</param>
public sealed class NoticeDeletedEvent(SysNotice entity) : AggregateDeletedEvent<SysNotice>(entity);

/// <summary>通知公告恢复领域事件：公告恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="entity">恢复后的通知公告。</param>
public sealed class NoticeRestoredEvent(SysNotice entity) : AggregateRestoredEvent<SysNotice>(entity);
