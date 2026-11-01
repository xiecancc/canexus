
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 租户实体：平台级全局表（租户定义不参与租户隔离过滤）。
/// <para>归属业务数据（如 SysUser）通过其 TenantId 字段（取值为本实体的 Id，即 Guid 主键）建立租户归属关系；TenantKey 为业务侧租户编码。
/// 实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；TenantKey 唯一性由业务层校验保证。</para>
/// </summary>
[SugarTable("sys_tenant")]
public class SysTenant : AggregateBase
{
    /// <summary>租户标识（唯一，即请求头 X-Tenant-Id 的取值，由业务层校验保证）。</summary>
    [SugarColumn(Length = 50)]
    public virtual string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    [SugarColumn(Length = 50)]
    public virtual string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public virtual string? ContactPhone { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>过期时间（可空，不强制校验）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual DateTime? ExpireTime { get; set; }
}
