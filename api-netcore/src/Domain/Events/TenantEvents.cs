namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>租户变更领域事件：租户创建 / 更新 / 删除成功后发布（租户标识不可变更，无迁移语义）。</summary>
/// <param name="TenantId">变更的租户 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class TenantChangedEvent(Guid tenantId, AggregateChangeType changeType)
    : AggregateChangedEvent("tenant.changed", tenantId, changeType)
{
    /// <summary>变更的租户 Id。</summary>
    public Guid TenantId { get; } = tenantId;
}
