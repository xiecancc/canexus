namespace Domain.Events;

using Domain.Entities;

/// <summary>部门创建领域事件：部门创建成功后发布，携带创建后的部门状态。</summary>
/// <param name="Entity">创建后的部门。</param>
public sealed class DeptCreatedEvent(SysDept entity) : CreatedEvent<SysDept>("dept.created", entity);

/// <summary>部门修改领域事件：部门更新（含挂载点变化）成功后发布，携带变更前后状态。部门树变更可能影响数据范围（DeptAndBelow 等）的解析结果。</summary>
/// <param name="Before">变更前的部门快照。</param>
/// <param name="After">变更后的部门。</param>
public sealed class DeptUpdatedEvent(SysDept before, SysDept after) : UpdatedEvent<SysDept>("dept.updated", before, after);

/// <summary>部门删除领域事件：部门删除成功后发布，携带删除前的部门状态。</summary>
/// <param name="Entity">删除前的部门。</param>
public sealed class DeptDeletedEvent(SysDept entity) : DeletedEvent<SysDept>("dept.deleted", entity);
