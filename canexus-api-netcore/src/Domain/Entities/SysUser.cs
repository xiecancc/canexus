using Domain.Shared.Constants;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using SqlSugar;

namespace Domain.Entities;

/// <summary>系统用户实体。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；用户名唯一性由业务层校验 + 数据库唯一索引（含软删除键槽）双重保证。</para>
/// <para>聚合不变量（内置超管账号保护）以守卫方法内聚于本实体，命令处理器不得散落复刻。</para></summary>
[SugarIndex("uk_sys_user_name", nameof(UserName), OrderByType.Asc, nameof(DeletedKey), OrderByType.Asc, true)]
[SugarIndex("ix_sys_user_dept", nameof(DeptId), OrderByType.Asc)]
[SugarIndex("ix_sys_user_scope", nameof(TenantId), OrderByType.Asc, nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_user", TableDescription = "系统用户实体。")]
public class SysUser : AggregateBase
{
    /// <summary>用户名（唯一：业务层查重 + 数据库唯一索引双重保证）。</summary>
    [SugarColumn(ColumnDescription = "用户名（唯一，由业务层校验保证）。", Length = 30)]
    public virtual string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    [SugarColumn(ColumnDescription = "用户昵称。", Length = 30)]
    public virtual string NickName { get; set; } = string.Empty;

    /// <summary>密码哈希（BCrypt）。安全约定：敏感字段，领域事件载荷序列化（Outbox）必须排除。</summary>
    [SugarColumn(ColumnDescription = "密码哈希（BCrypt）。安全约定：敏感字段，领域事件载荷序列化（Outbox）必须排除。", Length = 100)]
    [System.Text.Json.Serialization.JsonIgnore]
    public virtual string PasswordHash { get; set; } = string.Empty;

    /// <summary>所属部门 Id；null 表示未分配部门。</summary>
    [SugarColumn(ColumnDescription = "所属部门 Id；null 表示未分配部门。", IsNullable = true)]
    public virtual Guid? DeptId { get; set; }

    /// <summary>邮箱。</summary>
    [SugarColumn(ColumnDescription = "邮箱。", IsNullable = true, Length = 50)]
    public virtual string? Email { get; set; }

    /// <summary>手机号。</summary>
    [SugarColumn(ColumnDescription = "手机号。", IsNullable = true, Length = 20)]
    public virtual string? Phone { get; set; }

    /// <summary>头像地址。</summary>
    [SugarColumn(ColumnDescription = "头像地址。", IsNullable = true, Length = 200)]
    public virtual string? Avatar { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 启用，0 停用）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>
    /// 首次登录须强制改密标记（true 表示下次登录后必须先修改密码；null 视为 false）。
    /// <para>使用默认初始密码创建的用户与管理员重置密码后置为 true，登录成功后（自助改密完成）清除。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "首次登录须强制改密标记（true 表示下次登录后必须先修改密码；null 视为 false）。 使用默认初始密码创建的用户与管理员重置密码后置为 true，登录成功后（自助改密完成）清除。", IsNullable = true)]
    public virtual bool? MustChangePassword { get; set; }

    /// <summary>
    /// 令牌版本（用户级安全戳）：签入 accessToken 的 tokenVersion 声明，请求校验时与当前值比对；
    /// 改密 / 停用 / 删除 / 强制全量下线 / 角色重分配等操作递增该值，使此前签发的 accessToken 立即失效。
    /// <para>初始值 0；历史令牌缺少该声明时按 0 处理，保证滚动发布期间旧令牌平滑过渡（仅到自然过期）。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "令牌版本（用户级安全戳）：递增后该用户此前签发的 accessToken 立即失效。")]
    public virtual int TokenVersion { get; set; }

    /// <summary>是否内置超管账号（admin，种子数据初始化的保留账号）。</summary>
    public bool IsBuiltInAdmin => UserName == UserConstants.AdminUserName;

    /// <summary>聚合不变量守卫：内置超管账号 admin 拒绝删除、编辑、状态变更等管理操作。</summary>
    /// <param name="action">被拒绝的操作名（用于错误文案，如「删除」「编辑」「启停」）。</param>
    /// <exception cref="BusinessException">当前用户为内置超管账号时抛出（错误码 <see cref="ErrorCodes.UserAdminProtected"/>）。</exception>
    public void EnsureNotBuiltInAdmin(string action)
    {
        if (IsBuiltInAdmin)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, $"内置超管账号 admin 不允许{action}");
        }
    }
}
