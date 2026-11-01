namespace Application.Permissions;

/// <summary>
/// 权限校验器：解析用户最终权限标识集合（角色 → 角色菜单 → 菜单 perms）。
/// <para>按 userId 缓存（IMemoryCache，10 分钟）；角色 / 菜单变更时通过 <see cref="InvalidateAsync"/> 或
/// <see cref="InvalidateAllAsync"/> 主动失效。超管角色（super_admin）返回通配权限 <c>*:*:*</c>。</para>
/// </summary>
public interface IPermissionChecker
{
    /// <summary>获取用户权限标识集合（含缓存）。超管为 ["*:*:*"]；无角色 / 无权限返回空集合。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>权限标识集合。</returns>
    public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>判断用户是否拥有指定权限。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="permission">权限标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>拥有返回 true。</returns>
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);

    /// <summary>失效指定用户的权限缓存（角色分配、角色菜单变更时调用）。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>失效全部用户的权限缓存（角色 / 菜单本身变更时调用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}
