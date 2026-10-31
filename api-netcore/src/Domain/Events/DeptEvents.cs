namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>部门变更领域事件：部门创建 / 更新（含挂载点变化）/ 删除成功后发布。部门树变更可能影响数据范围（DeptAndBelow 等）的解析结果。</summary>
/// <param name="DeptId">变更的部门 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class DeptChangedEvent(Guid deptId, AggregateChangeType changeType)
    : DomainEvent("dept.changed", deptId, new Dictionary<string, object?>
    {
        ["deptId"] = deptId,
        ["changeType"] = changeType.ToString()
    })
{
    /// <summary>变更的部门 Id。</summary>
    public Guid DeptId { get; } = deptId;

    /// <summary>变更类型。</summary>
    public AggregateChangeType ChangeType { get; } = changeType;
}
