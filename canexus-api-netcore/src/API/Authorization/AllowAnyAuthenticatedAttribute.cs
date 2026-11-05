namespace API.Authorization;

/// <summary>
/// 登录即可访问豁免标记（P0 权限默认拒绝）：标注在「任何已认证用户均可访问、无需细粒度权限」的端点上
/// （如登出、自助改密、用户信息、前端路由），使权限过滤器与启动期反向扫描放行。
/// <para>默认拒绝原则：受保护端点必须声明 <see cref="RequirePermissionAttribute"/> 或本标记，
/// 二者皆无的端点在启动期校验失败、运行期被过滤器拒绝——显式选择放行，而非隐式遗漏。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnyAuthenticatedAttribute : Attribute
{
}
