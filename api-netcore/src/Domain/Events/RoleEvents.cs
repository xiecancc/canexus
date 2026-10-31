namespace Domain.Events;

using Domain.Entities;

/// <summary>角色创建领域事件：角色创建成功后发布，携带创建后的角色状态。新角色尚未被用户持有，通常无权限影响。</summary>
/// <param name="Entity">创建后的角色。</param>
public sealed class RoleCreatedEvent(SysRole entity) : CreatedEvent<SysRole>("role.created", entity);

/// <summary>角色修改领域事件：角色更新（含授权菜单、数据范围、状态）成功后发布，携带变更前后状态。角色 key / 状态 / 授权菜单变更可能影响全部持有该角色用户的权限。</summary>
/// <param name="Before">变更前的角色快照。</param>
/// <param name="After">变更后的角色。</param>
public sealed class RoleUpdatedEvent(SysRole before, SysRole after) : UpdatedEvent<SysRole>("role.updated", before, after);

/// <summary>角色删除领域事件：角色删除成功后发布，携带删除前的角色状态。</summary>
/// <param name="Entity">删除前的角色。</param>
public sealed class RoleDeletedEvent(SysRole entity) : DeletedEvent<SysRole>("role.deleted", entity);
