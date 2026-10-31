namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>角色变更领域事件：角色创建 / 更新（含授权菜单、数据范围、状态）/ 删除成功后发布。角色变更可能影响全部持有该角色用户的权限。</summary>
/// <param name="RoleId">变更的角色 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class RoleChangedEvent(Guid roleId, AggregateChangeType changeType)
    : AggregateChangedEvent("role.changed", roleId, changeType)
{
    /// <summary>变更的角色 Id。</summary>
    public Guid RoleId { get; } = roleId;
}
