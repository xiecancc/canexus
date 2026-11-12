
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace API.Authorization;
/// <summary>
/// minimal API 端点策略启动期校验（评审 A3）：本项目约定「新增 HTTP 端点一律走 MVC 控制器」，
/// 由 <see cref="PermissionsStartupValidator"/> 做权限反向扫描；现存 minimal API 仅限健康检查与
/// OpenAPI 文档，且必须显式 <see cref="AllowAnonymousAttribute"/> 放行。
/// <para>本校验器在宿主启动前枚举最终端点集合：非 MVC 端点若未显式匿名，说明有人新增了受保护的
/// minimal API（只受 FallbackPolicy 认证约束，不受 [RequirePermission] / 强制改密拦截），
/// 此时 fail-closed 阻止启动，提醒改用控制器或补齐权限元数据。</para>
/// </summary>
public static class MinimalApiEndpointPolicyValidator
{
    /// <summary>返回未显式匿名放行的非 MVC 端点描述列表（按显示名升序）。</summary>
    /// <param name="endpoints">宿主最终端点集合（<c>EndpointDataSource</c>）。</param>
    /// <returns>违规端点描述；全部合规时为空。</returns>
    public static IReadOnlyList<string> FindNonAnonymousMinimalEndpoints(EndpointDataSource endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(endpoint => endpoint.DisplayName ?? "(未命名端点)")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>启动期校验：存在未显式匿名的非 MVC 端点时抛出 <see cref="InvalidOperationException"/> 阻止启动。</summary>
    /// <param name="endpoints">宿主最终端点集合。</param>
    /// <exception cref="InvalidOperationException">存在不合规 minimal API 端点时抛出，消息列出全部端点。</exception>
    public static void Validate(EndpointDataSource endpoints)
    {
        var violations = FindNonAnonymousMinimalEndpoints(endpoints);
        if (violations.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "minimal API 端点策略校验失败：以下非控制器端点既未显式匿名，也不受权限过滤器 / 强制改密保护。\n"
            + "请改用 MVC 控制器并标注 [RequirePermission]，或确为匿名探针时补 .AllowAnonymous()：\n"
            + string.Join("\n", violations));
    }
}
