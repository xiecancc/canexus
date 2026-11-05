
using Domain.Entities;

namespace Domain.Events;
/// <summary>菜单创建领域事件：菜单创建成功后发布，Domain 为创建后的菜单状态。新菜单可能携带 perms 并被分配角色。</summary>
/// <param name="newEntity">创建后的菜单。</param>
public sealed class MenuCreatedEvent(SysMenu newEntity) : AggregateCreatedEvent<SysMenu>(newEntity);

/// <summary>菜单修改领域事件：菜单更新（含挂载点变化）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。菜单 perms / 挂载点变更可能影响全部已授权角色的权限。</summary>
/// <param name="oldEntity">变更前的菜单快照。</param>
/// <param name="newEntity">变更后的菜单。</param>
public sealed class MenuUpdatedEvent(SysMenu oldEntity, SysMenu newEntity) : AggregateUpdatedEvent<SysMenu>(oldEntity, newEntity);

/// <summary>菜单删除领域事件：菜单删除成功后发布，Domain 为删除前的菜单状态。</summary>
/// <param name="newEntity">删除前的菜单。</param>
public sealed class MenuDeletedEvent(SysMenu newEntity) : AggregateDeletedEvent<SysMenu>(newEntity);

/// <summary>菜单恢复领域事件：菜单恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的菜单。</param>
public sealed class MenuRestoredEvent(SysMenu newEntity) : AggregateRestoredEvent<SysMenu>(newEntity);
