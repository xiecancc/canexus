
using Domain.Shared.Constants;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using SqlSugar;

namespace Domain.Entities;
/// <summary>系统角色实体。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；角色权限字符串唯一性由业务层校验保证。</para>
/// <para>聚合不变量（内置超管角色保护与保留权限字符）以守卫方法内聚于本实体，命令处理器不得散落复刻。</para></summary>
[SugarTable("sys_role", TableDescription = "系统角色实体。")]
public class SysRole : AggregateBase
{
    /// <summary>角色名称（显示用）。</summary>
    [SugarColumn(ColumnDescription = "角色名称（显示用）。", Length = 30)]
    public virtual string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符串（唯一，由业务层校验保证）；super_admin 为内置超管，放行全部权限。</summary>
    [SugarColumn(ColumnDescription = "角色权限字符串（唯一，由业务层校验保证）；super_admin 为内置超管，放行全部权限。", Length = 100)]
    public virtual string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（见 <see cref="Domain.Shared.Enums.DataScope"/>）。</summary>
    [SugarColumn(ColumnDescription = "数据范围（见 DataScope）。")]
    public virtual DataScope DataScope { get; set; } = DataScope.All;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 启用，0 停用）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>是否内置超管角色（super_admin，种子数据初始化的保留角色，放行全部权限）。</summary>
    public bool IsSuperAdmin => RoleKey == RoleConstants.SuperAdminRoleKey;

    /// <summary>聚合不变量守卫：内置超管角色 super_admin 拒绝删除、编辑、启停等管理操作。</summary>
    /// <param name="action">被拒绝的操作名（用于错误文案，如「删除」「编辑」「启停」）。</param>
    /// <exception cref="BusinessException">当前角色为内置超管角色时抛出（错误码 <see cref="ErrorCodes.RoleProtected"/>）。</exception>
    public void EnsureNotSuperAdmin(string action)
    {
        if (IsSuperAdmin)
        {
            throw new BusinessException(ErrorCodes.RoleProtected, $"内置超管角色 super_admin 不允许{action}");
        }
    }

    /// <summary>聚合不变量守卫：角色权限字符不可使用内置保留值 super_admin（创建与修改共用）。</summary>
    /// <param name="roleKey">待校验的角色权限字符。</param>
    /// <exception cref="BusinessException">权限字符为保留值时抛出（错误码 <see cref="ErrorCodes.RoleKeyReserved"/>）。</exception>
    public static void EnsureRoleKeyNotReserved(string roleKey)
    {
        if (roleKey == RoleConstants.SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleKeyReserved, "角色权限字符 super_admin 为内置保留值，不可使用");
        }
    }
}
