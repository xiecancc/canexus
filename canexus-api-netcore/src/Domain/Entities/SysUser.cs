using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;

/// <summary>系统用户实体。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；用户名唯一性由业务层校验保证。</para></summary>
[SugarTable("sys_user")]
public class SysUser : AggregateBase
{
    /// <summary>用户名（唯一，由业务层校验保证）。</summary>
    [SugarColumn(Length = 30)]
    public virtual string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    [SugarColumn(Length = 30)]
    public virtual string NickName { get; set; } = string.Empty;

    /// <summary>密码哈希（BCrypt）。</summary>
    [SugarColumn(Length = 100)]
    public virtual string PasswordHash { get; set; } = string.Empty;

    /// <summary>所属部门 Id；null 表示未分配部门。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual Guid? DeptId { get; set; }

    /// <summary>邮箱。</summary>
    [SugarColumn(IsNullable = true, Length = 50)]
    public virtual string? Email { get; set; }

    /// <summary>手机号。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public virtual string? Phone { get; set; }

    /// <summary>头像地址。</summary>
    [SugarColumn(IsNullable = true, Length = 200)]
    public virtual string? Avatar { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
