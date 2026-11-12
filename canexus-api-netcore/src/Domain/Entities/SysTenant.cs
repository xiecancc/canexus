
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 租户实体：平台级全局表（租户定义的行 TenantId IS NULL）。
/// <para>可见性规则（见 SqlSugarClientFactory 全局过滤器）：平台用户 / 匿名请求（JWT 无 tenantId 声明）
/// 在任何租户上下文下均可读取平台级行；租户用户仅可见本租户业务数据，平台级行对其不可见。
/// TenantAccessValidator 校验租户可用性时走 .ClearFilter&lt;SysTenant&gt;() 绕开租户过滤，
/// 避免「用目标租户的过滤条件去查平台级租户定义」造成冷缓存误判（V-01）。</para>
/// <para>归属业务数据（如 SysUser）通过其 TenantId 字段（取值为本实体的 Id，即 Guid 主键）建立租户归属关系；TenantKey 为业务侧租户编码。
/// 实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；TenantKey 唯一性由业务层校验保证。</para>
/// </summary>
[SugarIndex("uk_sys_tenant_key", nameof(TenantKey), OrderByType.Asc, nameof(DeletedKey), OrderByType.Asc, true)]
[SugarIndex("ix_sys_tenant_scope", nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_tenant", TableDescription = "租户实体：平台级全局表（租户定义不参与租户隔离过滤）。")]
public class SysTenant : AggregateBase
{
    /// <summary>租户标识（唯一，即请求头 X-Tenant-Id 的取值，由业务层校验保证）。</summary>
    [SugarColumn(ColumnDescription = "租户标识（唯一，即请求头 X-Tenant-Id 的取值，由业务层校验保证）。", Length = 50)]
    public virtual string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    [SugarColumn(ColumnDescription = "租户名称。", Length = 50)]
    public virtual string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    [SugarColumn(ColumnDescription = "联系电话。", IsNullable = true, Length = 20)]
    public virtual string? ContactPhone { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 启用，0 停用）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>过期时间（可空，不强制校验）。</summary>
    [SugarColumn(ColumnDescription = "过期时间（可空，不强制校验）。", IsNullable = true)]
    public virtual DateTime? ExpireTime { get; set; }
}
