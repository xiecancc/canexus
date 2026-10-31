namespace Domain.Shared.Enums;

/// <summary>聚合根变更类型：领域事件载荷中标注的业务动作语义（创建 / 更新 / 删除）。</summary>
public enum AggregateChangeType
{
    /// <summary>创建。</summary>
    Created = 1,

    /// <summary>更新（含字段修改、关联重分配、状态调整）。</summary>
    Updated = 2,

    /// <summary>删除（含软删除）。</summary>
    Deleted = 3,

    /// <summary>重置密码（用户域专用：常关联强制下线等后续动作）。</summary>
    PasswordReset = 4
}
