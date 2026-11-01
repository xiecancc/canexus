namespace Domain.Services.Users;

/// <summary>
/// 当前用户上下文：基于认证主体（JWT claims）解析的当前用户完整信息。
/// <para>统一入口：权限校验、数据范围过滤、审计字段填充等场景均经本接口取当前用户。</para>
/// </summary>
public interface IUserContext
{
    /// <summary>当前用户 Id；未认证时为 null。</summary>
    public Guid? UserId { get; }

    /// <summary>当前用户名；未认证时为 null。</summary>
    public string? UserName { get; }

    /// <summary>当前用户所属部门 Id；未分配或未认证时为 null。</summary>
    public Guid? DeptId { get; }

    /// <summary>当前用户的角色 Id 集合。</summary>
    public IReadOnlyList<Guid> RoleIds { get; }

    /// <summary>当前用户的角色权限字符串（roleKey）集合。</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>当前用户所属租户 Id；null 表示平台级。</summary>
    public Guid? TenantId { get; }

    /// <summary>是否已通过认证。</summary>
    public bool IsAuthenticated { get; }

    /// <summary>是否超级管理员（任一角色 roleKey 为 super_admin，放行全部权限）。</summary>
    public bool IsSuperAdmin { get; }
}
