namespace Domain.Events;

using Domain.Entities;

/// <summary>菜单创建领域事件：菜单创建成功后发布，携带创建后的菜单状态。新菜单可能携带 perms 并被分配角色。</summary>
/// <param name="Entity">创建后的菜单。</param>
public sealed class MenuCreatedEvent(SysMenu entity) : CreatedEvent<SysMenu>("menu.created", entity);

/// <summary>菜单修改领域事件：菜单更新（含挂载点变化）成功后发布，携带变更前后状态。菜单 perms / 挂载点变更可能影响全部已授权角色的权限。</summary>
/// <param name="Before">变更前的菜单快照。</param>
/// <param name="After">变更后的菜单。</param>
public sealed class MenuUpdatedEvent(SysMenu before, SysMenu after) : UpdatedEvent<SysMenu>("menu.updated", before, after);

/// <summary>菜单删除领域事件：菜单删除成功后发布，携带删除前的菜单状态。</summary>
/// <param name="Entity">删除前的菜单。</param>
public sealed class MenuDeletedEvent(SysMenu entity) : DeletedEvent<SysMenu>("menu.deleted", entity);
