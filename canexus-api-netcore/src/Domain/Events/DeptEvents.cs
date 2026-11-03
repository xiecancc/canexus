
using Domain.Entities;

namespace Domain.Events;
/// <summary>部门创建领域事件：部门创建成功后发布，Domain 为创建后的部门状态。</summary>
/// <param name="entity">创建后的部门。</param>
public sealed class DeptCreatedEvent(SysDept entity) : AggregateCreatedEvent<SysDept>(entity);

/// <summary>部门修改领域事件：部门更新（含挂载点变化）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。部门树变更可能影响数据范围（DeptAndBelow 等）的解析结果。</summary>
/// <param name="before">变更前的部门快照。</param>
/// <param name="after">变更后的部门。</param>
public sealed class DeptUpdatedEvent(SysDept before, SysDept after) : AggregateUpdatedEvent<SysDept>(before, after);

/// <summary>部门删除领域事件：部门删除成功后发布，Domain 为删除前的部门状态。</summary>
/// <param name="entity">删除前的部门。</param>
public sealed class DeptDeletedEvent(SysDept entity) : AggregateDeletedEvent<SysDept>(entity);

/// <summary>部门恢复领域事件：部门恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="entity">恢复后的部门。</param>
public sealed class DeptRestoredEvent(SysDept entity) : AggregateRestoredEvent<SysDept>(entity);
