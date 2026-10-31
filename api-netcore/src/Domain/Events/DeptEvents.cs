namespace Domain.Events;

using Domain.Entities;

/// <summary>部门创建领域事件：部门创建成功后发布，Domain 为创建后的部门状态。</summary>
/// <param name="Entity">创建后的部门。</param>
public sealed class DeptCreatedEvent(SysDept entity) : AggregateCreatedEvent<SysDept>(entity);

/// <summary>部门修改领域事件：部门更新（含挂载点变化）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。部门树变更可能影响数据范围（DeptAndBelow 等）的解析结果。</summary>
/// <param name="Before">变更前的部门快照。</param>
/// <param name="After">变更后的部门。</param>
public sealed class DeptUpdatedEvent(SysDept before, SysDept after) : AggregateUpdatedEvent<SysDept>(before, after);

/// <summary>部门删除领域事件：部门删除成功后发布，Domain 为删除前的部门状态。</summary>
/// <param name="Entity">删除前的部门。</param>
public sealed class DeptDeletedEvent(SysDept entity) : AggregateDeletedEvent<SysDept>(entity);
