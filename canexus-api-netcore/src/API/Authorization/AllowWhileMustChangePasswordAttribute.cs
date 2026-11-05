namespace API.Authorization;

/// <summary>
/// 强制改密豁免标记：标注在待改密用户（MustChangePassword=true）仍需访问的端点上
/// （如自助改密、用户信息、路由与登出），使强制改密中间件放行这些请求。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhileMustChangePasswordAttribute : Attribute
{
}
