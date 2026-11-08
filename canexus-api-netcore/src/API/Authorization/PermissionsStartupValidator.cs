using System.Reflection;
using Domain.Shared.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;


namespace API.Authorization;

/// <summary>
/// 权限声明启动期校验器（P2-B4 / P0 默认拒绝）：双向扫描 API 程序集控制器。
/// <list type="bullet">
/// <item>正向：扫描全部 [RequirePermission] 声明，与集中注册表 <see cref="PermissionDefinitions"/> 比对，
/// 未声明权限在启动期直接失败，杜绝散落字符串失配；</item>
/// <item>反向（默认拒绝）：每个 Action 必须显式声明「匿名（[AllowAnonymous]）/ 需权限（[RequirePermission]）/
/// 登录即可（[AllowAnyAuthenticated]」三者之一；受保护端点漏写权限声明、或既未授权也未匿名
/// （默认匿名可达）时启动失败——显式选择放行，而非隐式遗漏。</item>
/// </list>
/// <para>Program.cs 在宿主启动前调用 <see cref="Validate"/>；测试通过
/// <see cref="FindUndeclaredFromAssembly"/> / <see cref="FindUnauthorizedEndpoints"/> 复用同一实现。</para>
/// </summary>
public static class PermissionsStartupValidator
{
    /// <summary>收集程序集中全部 [RequirePermission] 声明的权限标识（类级与 Action 级，去重）。</summary>
    /// <param name="assembly">待扫描的程序集。</param>
    /// <returns>去重后的权限标识集合。</returns>
    public static IReadOnlyCollection<string> CollectPermissions(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var permissions = assembly.GetTypes()
            .SelectMany(type => CollectFromType(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return permissions;
    }

    /// <summary>扫描单个类型（含其全部方法）上的 [RequirePermission] 声明。</summary>
    private static IEnumerable<string> CollectFromType(Type type)
    {
        foreach (var attribute in type.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))
        {
            yield return attribute.Permission;
        }

        foreach (var method in GetActionMethods(type))
        {
            foreach (var attribute in method.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))
            {
                yield return attribute.Permission;
            }
        }
    }

    /// <summary>返回程序集中未在注册表声明的权限标识列表。</summary>
    /// <param name="assembly">待扫描的程序集。</param>
    /// <returns>未声明的权限标识列表（无则为空）。</returns>
    public static IReadOnlyList<string> FindUndeclaredFromAssembly(Assembly assembly)
    {
        return PermissionRegistryValidator.FindUndeclared(CollectPermissions(assembly));
    }

    /// <summary>
    /// 反向扫描（P0 权限默认拒绝）：返回程序集中权限声明不合规的端点列表（"Controller.Action + 原因"）。
    /// <para>规则（对全部 <see cref="ControllerBase"/> 派生的公开类的 HTTP 方法端点生效）：
    /// Action 标注 [AllowAnonymous] 放行；否则必须有 [RequirePermission]；
    /// 或同时具备授权语义（[Authorize]）与 [AllowAnyAuthenticated] 豁免；
    /// 其余情形（受保护但漏写权限声明、或既未授权也未匿名导致默认匿名可达）均视为违规。</para>
    /// </summary>
    /// <param name="assembly">待扫描的程序集。</param>
    /// <returns>违规端点描述列表（无则为空）。</returns>
    public static IReadOnlyList<string> FindUnauthorizedEndpoints(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var violations = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            // 可见控制器（含测试程序集中的嵌套样例控制器）；抽象类无端点跳过
            if (!typeof(ControllerBase).IsAssignableFrom(type) || (!type.IsPublic && !type.IsNestedPublic) || type.IsAbstract)
            {
                continue;
            }

            var typeAllowAnonymous = type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            var typeRequirePermission = type.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Any();
            var typeAllowAnyAuthenticated = type.GetCustomAttributes<AllowAnyAuthenticatedAttribute>(inherit: true).Any();
            var typeAuthorize = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

            foreach (var method in GetActionMethods(type))
            {
                var actionName = $"{type.Name}.{method.Name}";
                var actionAllowAnonymous = method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                if (typeAllowAnonymous || actionAllowAnonymous)
                {
                    continue;
                }

                var actionRequirePermission = method.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Any();
                var actionAllowAnyAuthenticated = method.GetCustomAttributes<AllowAnyAuthenticatedAttribute>(inherit: true).Any();
                var actionAuthorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

                if (typeRequirePermission || actionRequirePermission)
                {
                    continue;
                }

                if (actionAllowAnyAuthenticated || typeAllowAnyAuthenticated)
                {
                    // 登录即可豁免：仍须有授权语义（[Authorize]），否则豁免无效、默认匿名可达
                    if (typeAuthorize || actionAuthorize)
                    {
                        continue;
                    }

                    violations.Add($"{actionName}：标注了 [AllowAnyAuthenticated] 但缺少 [Authorize]，默认匿名可达");
                    continue;
                }

                violations.Add($"{actionName}：受保护端点未声明权限（须标注 [RequirePermission]、[AllowAnonymous] 或 [AllowAnyAuthenticated]）");
            }
        }

        return violations;
    }

    /// <summary>启动期校验：权限未声明（正向）或端点权限声明缺失（反向）时抛出 <see cref="InvalidOperationException"/>，阻止宿主启动。</summary>
    /// <param name="assembly">待扫描的程序集。</param>
    public static void Validate(Assembly assembly)
    {
        PermissionRegistryValidator.EnsureDeclared(CollectPermissions(assembly));

        var unauthorized = FindUnauthorizedEndpoints(assembly);
        if (unauthorized.Count > 0)
        {
            throw new InvalidOperationException(
                "权限默认拒绝校验失败：以下端点未显式声明访问策略（[RequirePermission] / [AllowAnonymous] / [AllowAnyAuthenticated]）：\n"
                + string.Join("\n", unauthorized));
        }
    }

    /// <summary>
    /// 枚举类型上的 Action 方法：公开实例方法（含从基类继承的 Action，避免漏扫继承端点），
    /// 且带 HTTP 方法特性（GET / POST / PUT / DELETE / PATCH / HEAD / OPTIONS）。
    /// </summary>
    private static IEnumerable<MethodInfo> GetActionMethods(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            {
                yield return method;
            }
        }
    }
}
