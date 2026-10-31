namespace Domain.Events;

using Domain.Entities;

/// <summary>租户创建领域事件：租户创建成功后发布，Domain 为创建后的状态（租户初始化等响应动作可由监听器扩展处理）。</summary>
/// <param name="Entity">创建后的租户。</param>
public sealed class TenantCreatedEvent(SysTenant entity) : AggregateCreatedEvent<SysTenant>(entity);

/// <summary>租户修改领域事件：租户更新成功后发布，Domain 为变更后状态、Before 为变更前快照（租户标识不可变更，无迁移语义）。</summary>
/// <param name="Before">变更前的租户快照。</param>
/// <param name="After">变更后的租户。</param>
public sealed class TenantUpdatedEvent(SysTenant before, SysTenant after) : AggregateUpdatedEvent<SysTenant>(before, after);

/// <summary>租户删除领域事件：租户删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="Entity">删除前的租户。</param>
public sealed class TenantDeletedEvent(SysTenant entity) : AggregateDeletedEvent<SysTenant>(entity);
