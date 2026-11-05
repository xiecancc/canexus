
using Domain.Entities;

namespace Domain.Events;
/// <summary>部门创建领域事件：部门创建成功后发布，Domain 为创建后的部门状态。</summary>
/// <param name="newEntity">创建后的部门。</param>
public sealed class DeptCreatedEvent(SysDept newEntity) : AggregateCreatedEvent<SysDept>(newEntity);

/// <summary>部门修改领域事件：部门更新（含挂载点变化）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。部门树变更可能影响数据范围（DeptAndBelow 等）的解析结果。</summary>
/// <param name="oldEntity">变更前的部门快照。</param>
/// <param name="newEntity">变更后的部门。</param>
public sealed class DeptUpdatedEvent(SysDept oldEntity, SysDept newEntity) : AggregateUpdatedEvent<SysDept>(oldEntity, newEntity);

/// <summary>部门删除领域事件：部门删除成功后发布，Domain 为删除前的部门状态。</summary>
/// <param name="newEntity">删除前的部门。</param>
public sealed class DeptDeletedEvent(SysDept newEntity) : AggregateDeletedEvent<SysDept>(newEntity);

/// <summary>部门恢复领域事件：部门恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的部门。</param>
public sealed class DeptRestoredEvent(SysDept newEntity) : AggregateRestoredEvent<SysDept>(newEntity);
