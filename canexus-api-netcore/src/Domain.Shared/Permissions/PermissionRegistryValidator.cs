namespace Domain.Shared.Permissions;

/// <summary>
/// 权限注册表校验器（P2-B4）：比对一组权限标识与集中注册表，发现未声明权限。
/// <para>供 API 启动期校验与测试使用：新增权限必须先在 <see cref="PermissionDefinitions"/> 声明，
/// 否则控制器引用将无法通过启动校验（或在测试中直接暴露）。</para>
/// </summary>
public static class PermissionRegistryValidator
{
    /// <summary>返回未在注册表中声明的权限标识（输入去重后按原始顺序保留）。</summary>
    /// <param name="permissions">待校验的权限标识集合。</param>
    /// <returns>未声明的权限标识列表（无则为空）。</returns>
    public static IReadOnlyList<string> FindUndeclared(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var declared = PermissionDefinitions.All;
        return permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission) && !declared.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>校验一组权限标识均已在注册表中声明；存在未声明项时抛出 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="permissions">待校验的权限标识集合。</param>
    /// <exception cref="InvalidOperationException">存在未声明权限时抛出，异常信息列出全部未声明项。</exception>
    public static void EnsureDeclared(IEnumerable<string> permissions)
    {
        var undeclared = FindUndeclared(permissions);
        if (undeclared.Count > 0)
        {
            throw new InvalidOperationException(
                $"存在未在 PermissionDefinitions 中声明的权限标识：{string.Join(", ", undeclared)}。请先在注册表集中声明。");
        }
    }
}
