namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>用户变更领域事件：用户创建 / 更新（含角色分配、状态调整）/ 重置密码 / 删除成功后发布。</summary>
/// <param name="UserId">变更的用户 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class UserChangedEvent(Guid userId, AggregateChangeType changeType)
    : DomainEvent("user.changed", userId, new Dictionary<string, object?>
    {
        ["userId"] = userId,
        ["changeType"] = changeType.ToString()
    })
{
    /// <summary>变更的用户 Id。</summary>
    public Guid UserId { get; } = userId;

    /// <summary>变更类型。</summary>
    public AggregateChangeType ChangeType { get; } = changeType;
}
