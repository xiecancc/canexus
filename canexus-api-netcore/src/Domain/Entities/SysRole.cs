
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>系统角色实体。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；角色权限字符串唯一性由业务层校验保证。</para></summary>
[SugarTable("sys_role")]
public class SysRole : AggregateBase
{
    /// <summary>角色名称（显示用）。</summary>
    [SugarColumn(Length = 30)]
    public virtual string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符串（唯一，由业务层校验保证）；super_admin 为内置超管，放行全部权限。</summary>
    [SugarColumn(Length = 100)]
    public virtual string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（见 <see cref="Domain.Shared.Enums.DataScope"/>）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual DataScope DataScope { get; set; } = DataScope.All;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
