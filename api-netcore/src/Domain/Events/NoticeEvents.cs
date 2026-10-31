namespace Domain.Events;

using Domain.Entities;

/// <summary>通知公告创建领域事件：公告创建成功后发布，携带创建后的状态（消息推送等响应动作可由监听器扩展处理）。</summary>
/// <param name="Entity">创建后的通知公告。</param>
public sealed class NoticeCreatedEvent(SysNotice entity) : CreatedEvent<SysNotice>("notice.created", entity);

/// <summary>通知公告修改领域事件：公告更新成功后发布，携带变更前后状态。</summary>
/// <param name="Before">变更前的公告快照。</param>
/// <param name="After">变更后的公告。</param>
public sealed class NoticeUpdatedEvent(SysNotice before, SysNotice after) : UpdatedEvent<SysNotice>("notice.updated", before, after);

/// <summary>通知公告删除领域事件：公告删除成功后发布，携带删除前的状态。</summary>
/// <param name="Entity">删除前的通知公告。</param>
public sealed class NoticeDeletedEvent(SysNotice entity) : DeletedEvent<SysNotice>("notice.deleted", entity);
