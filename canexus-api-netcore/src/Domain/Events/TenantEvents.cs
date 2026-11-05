
using Domain.Entities;

namespace Domain.Events;
/// <summary>租户创建领域事件：租户创建成功后发布，Domain 为创建后的状态（租户初始化等响应动作可由监听器扩展处理）。</summary>
/// <param name="newEntity">创建后的租户。</param>
public sealed class TenantCreatedEvent(SysTenant newEntity) : AggregateCreatedEvent<SysTenant>(newEntity);

/// <summary>租户修改领域事件：租户更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照（租户标识不可变更，无迁移语义）。</summary>
/// <param name="oldEntity">变更前的租户快照。</param>
/// <param name="newEntity">变更后的租户。</param>
public sealed class TenantUpdatedEvent(SysTenant oldEntity, SysTenant newEntity) : AggregateUpdatedEvent<SysTenant>(oldEntity, newEntity);

/// <summary>租户删除领域事件：租户删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的租户。</param>
public sealed class TenantDeletedEvent(SysTenant newEntity) : AggregateDeletedEvent<SysTenant>(newEntity);

/// <summary>租户恢复领域事件：租户恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的租户。</param>
public sealed class TenantRestoredEvent(SysTenant newEntity) : AggregateRestoredEvent<SysTenant>(newEntity);
