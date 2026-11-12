using System.Reflection;
using Application.Contracts.Abstractions;
using Domain.Shared.Permissions;

namespace API.Authorization;
/// <summary>
/// 命令级权限声明启动期校验器（P0-5）：反射扫描 Application.Contracts 中全部具体 <see cref="ICommand"/> 实现，
/// 要求每个写命令显式声明 [RequireCommandPermission] / [AllowAnonymousCommand] / [AllowAnyAuthenticatedCommand]
/// 三者之一（默认拒绝、显式放行），并核对全部 [RequireCommandPermission] 权限标识已在
/// <see cref="PermissionDefinitions"/> 注册表中声明。
/// <para>运行期 PermissionBehavior 对未标注命令默认放行，故漏标必须在启动期 fail-closed 阻止宿主启动。</para>
/// </summary>
public static class CommandPermissionStartupValidator
{
    /// <summary>返回未声明任何权限策略的具体写命令类型全名（按名称升序）。</summary>
    /// <param name="contractsAssembly">写命令所在程序集（Application.Contracts）。</param>
    /// <returns>缺少权限声明的写命令全名列表；全部声明时为空。</returns>
    public static IReadOnlyList<string> FindUnpermittedCommands(Assembly contractsAssembly)
    {
        ArgumentNullException.ThrowIfNull(contractsAssembly);

        return contractsAssembly.GetTypes()
            .Where(IsConcreteCommand)
            .Where(type => !HasPermissionPolicy(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>收集全部写命令上 [RequireCommandPermission] 声明的权限标识（去重）。</summary>
    /// <param name="contractsAssembly">写命令所在程序集（Application.Contracts）。</param>
    /// <returns>去重后的权限标识集合。</returns>
    public static IReadOnlyCollection<string> CollectCommandPermissions(Assembly contractsAssembly)
    {
        ArgumentNullException.ThrowIfNull(contractsAssembly);

        return contractsAssembly.GetTypes()
            .Where(IsConcreteCommand)
            .SelectMany(type => type.GetCustomAttributes<RequireCommandPermissionAttribute>(inherit: false))
            .Select(attribute => attribute.Permission)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>启动期校验：存在漏标命令或权限标识未在注册表声明时抛出 <see cref="InvalidOperationException"/> 阻止宿主启动。</summary>
    /// <param name="contractsAssembly">写命令所在程序集（Application.Contracts）。</param>
    /// <exception cref="InvalidOperationException">存在漏标命令或未声明权限时抛出，异常信息列出全部缺失项。</exception>
    public static void Validate(Assembly contractsAssembly)
    {
        var unpermitted = FindUnpermittedCommands(contractsAssembly);
        if (unpermitted.Count > 0)
        {
            throw new InvalidOperationException(
                "写命令权限声明校验失败：以下命令必须显式声明 [RequireCommandPermission] / [AllowAnonymousCommand] / [AllowAnyAuthenticatedCommand]：\n"
                + string.Join("\n", unpermitted));
        }

        PermissionRegistryValidator.EnsureDeclared(CollectCommandPermissions(contractsAssembly));
    }

    /// <summary>判断类型是否为可直接分发的写命令具体类型（非接口、非抽象、非泛型定义）。</summary>
    /// <param name="type">待判定的类型。</param>
    /// <returns>是具体写命令返回 true。</returns>
    private static bool IsConcreteCommand(Type type) =>
        type.IsClass && !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition
        && typeof(ICommand).IsAssignableFrom(type);

    /// <summary>判断类型是否声明了三种命令权限策略之一。</summary>
    /// <param name="type">待判定的写命令类型。</param>
    /// <returns>已声明任一策略返回 true。</returns>
    private static bool HasPermissionPolicy(Type type) =>
        type.IsDefined(typeof(AllowAnonymousCommandAttribute), inherit: false)
        || type.IsDefined(typeof(AllowAnyAuthenticatedCommandAttribute), inherit: false)
        || type.IsDefined(typeof(RequireCommandPermissionAttribute), inherit: false);
}
