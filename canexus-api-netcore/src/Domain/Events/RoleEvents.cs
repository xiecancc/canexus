
using Domain.Entities;

namespace Domain.Events;
/// <summary>角色创建领域事件：角色创建成功后发布，Domain 为创建后的角色状态。新角色尚未被用户持有，通常无权限影响。</summary>
/// <param name="Entity">创建后的角色。</param>
public sealed class RoleCreatedEvent(SysRole entity) : AggregateCreatedEvent<SysRole>(entity);

/// <summary>角色修改领域事件：角色更新（含授权菜单、数据范围、状态）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。角色 key / 状态 / 授权菜单变更可能影响全部持有该角色用户的权限。</summary>
/// <param name="Before">变更前的角色快照。</param>
/// <param name="After">变更后的角色。</param>
public sealed class RoleUpdatedEvent(SysRole before, SysRole after) : AggregateUpdatedEvent<SysRole>(before, after);

/// <summary>角色删除领域事件：角色删除成功后发布，Domain 为删除前的角色状态。</summary>
/// <param name="Entity">删除前的角色。</param>
public sealed class RoleDeletedEvent(SysRole entity) : AggregateDeletedEvent<SysRole>(entity);
