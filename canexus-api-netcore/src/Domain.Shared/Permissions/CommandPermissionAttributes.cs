namespace Domain.Shared.Permissions;

/// <summary>
/// 命令级权限要求特性：标注在写命令（<c>ICommand</c> 实现）上，声明执行该命令所需的权限标识
/// （须引用 <see cref="PermissionDefinitions"/> 中已声明的常量）。
/// <para>由命令权限管道在运行期解析并校验；由启动期校验器（CommandPermissionStartupValidator）
/// 扫描核对权限标识是否已在注册表声明，漏标或误标即阻止宿主启动（默认拒绝）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="permission">所需权限标识（应为 <see cref="PermissionDefinitions"/> 中已声明的常量）。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequireCommandPermissionAttribute(string permission) : Attribute
{
    /// <summary>所需权限标识。</summary>
    public string Permission { get; } = permission;
}

/// <summary>
/// 命令级匿名放行特性：标注在写命令（<c>ICommand</c> 实现）上，声明该命令无需认证即可执行
/// （如登录、刷新令牌）。运行期权限管道直接放行，不触发任何权限校验。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnonymousCommandAttribute : Attribute
{
}

/// <summary>
/// 命令级「登录即可」放行特性：标注在写命令（<c>ICommand</c> 实现）上，声明该命令只需通过认证
/// （如登出、自助改密），不校验细粒度权限。未认证时由权限管道拒绝（401）。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnyAuthenticatedCommandAttribute : Attribute
{
}
