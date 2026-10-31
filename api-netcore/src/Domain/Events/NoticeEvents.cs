namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>通知公告变更领域事件：公告创建 / 更新 / 删除成功后发布。</summary>
/// <param name="NoticeId">变更的通知公告 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class NoticeChangedEvent(Guid noticeId, AggregateChangeType changeType)
    : AggregateChangedEvent("notice.changed", noticeId, changeType)
{
    /// <summary>变更的通知公告 Id。</summary>
    public Guid NoticeId { get; } = noticeId;
}
