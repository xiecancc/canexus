namespace API.Authorization;

/// <summary>
/// 权限标识要求特性：标注在控制器或 Action 上，声明访问所需权限标识（如 system:user:list）。
/// <para>由全局权限过滤器（API 层）解析：从当前用户角色 → 角色菜单 → 菜单 perms 集合判断，
/// 无权限返回 403 统一响应；超管（roleKey=super_admin）放行全部。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    /// <summary>所需权限标识。</summary>
    public string Permission { get; }

    /// <summary>构造函数。</summary>
    /// <param name="permission">所需权限标识。</param>
    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }
}
