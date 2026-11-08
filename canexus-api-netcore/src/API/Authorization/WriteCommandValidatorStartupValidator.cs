
using System.Reflection;
using Application.Contracts.Abstractions;

namespace API.Authorization;
/// <summary>
/// 写命令验证器覆盖的启动期自检：反射扫描 Application.Contracts 中全部具体 <see cref="ICommand"/> 实现，
/// 与 Application 验证器程序集内注册的 <c>IValidator&lt;T&gt;</c> 闭合泛型集合比对，
/// 存在未覆盖的写命令时汇总后阻止宿主启动（ValidationBehavior 对无验证器请求直接放行，属静默缺口）。
/// </summary>
public static class WriteCommandValidatorStartupValidator
{
    /// <summary>FluentValidation 验证器接口的泛型定义全名（用名称比对，避免 API 层直接依赖验证器包）。</summary>
    private const string ValidatorInterfaceName = "FluentValidation.IValidator`1";

    /// <summary>返回未被任何验证器覆盖的具体写命令类型名（按名称升序）。</summary>
    /// <param name="contractsAssembly">写命令所在程序集（Application.Contracts）。</param>
    /// <param name="validatorsAssembly">验证器所在程序集（Application）。</param>
    /// <returns>缺验证器的写命令全名列表；全部覆盖时为空。</returns>
    public static IReadOnlyList<string> FindUnvalidatedCommands(Assembly contractsAssembly, Assembly validatorsAssembly)
    {
        ArgumentNullException.ThrowIfNull(contractsAssembly);
        ArgumentNullException.ThrowIfNull(validatorsAssembly);

        var validated = new HashSet<Type>();
        foreach (var validatorType in validatorsAssembly.GetTypes())
        {
            if (validatorType.IsAbstract || validatorType.IsInterface)
            {
                continue;
            }

            foreach (var implemented in validatorType.GetInterfaces())
            {
                if (implemented.IsGenericType
                    && string.Equals(implemented.GetGenericTypeDefinition().FullName, ValidatorInterfaceName, StringComparison.Ordinal))
                {
                    validated.Add(implemented.GetGenericArguments()[0]);
                }
            }
        }

        return contractsAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition
                && typeof(ICommand).IsAssignableFrom(type))
            .Where(type => !validated.Contains(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>启动期校验：存在未覆盖的写命令时抛出 <see cref="InvalidOperationException"/> 阻止宿主启动。</summary>
    /// <param name="contractsAssembly">写命令所在程序集（Application.Contracts）。</param>
    /// <param name="validatorsAssembly">验证器所在程序集（Application）。</param>
    public static void Validate(Assembly contractsAssembly, Assembly validatorsAssembly)
    {
        var missing = FindUnvalidatedCommands(contractsAssembly, validatorsAssembly);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "写命令验证器覆盖校验失败：以下命令没有任何 IValidator<T> 实现（ValidationBehavior 会直接放行）：\n"
                + string.Join("\n", missing));
        }
    }
}
