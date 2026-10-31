namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>菜单变更领域事件：菜单创建 / 更新（含挂载点变化）/ 删除成功后发布。菜单 perms 变更可能影响全部已授权角色的权限。</summary>
/// <param name="MenuId">变更的菜单 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class MenuChangedEvent(Guid menuId, AggregateChangeType changeType)
    : AggregateChangedEvent("menu.changed", menuId, changeType)
{
    /// <summary>变更的菜单 Id。</summary>
    public Guid MenuId { get; } = menuId;
}
