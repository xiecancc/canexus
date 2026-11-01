
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;

namespace Application.Permissions;
/// <summary>
/// 权限校验器实现：双层缓存按 userId 缓存权限集合（L1 2 分钟 / L2 10 分钟）。
/// <para>全局版本号存于缓存（<see cref="VersionCacheKey"/>，永不过期），全量失效时递增，旧 key 自然失配，无需遍历缓存。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
public sealed class PermissionChecker(
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainRepository<SysMenu> menuRepository,
    ICache cache) : IPermissionChecker
{
    /// <summary>缓存 key 前缀（key 格式：{prefix}{version}:{userId}）。</summary>
    public const string CacheKeyPrefix = "auth:permissions:";

    /// <summary>全局版本号缓存 key（永不过期：若过期回退 0，历史 v0 用户权限 key 会复活，产生越权窗口）。</summary>
    private const string VersionCacheKey = CacheKeyPrefix + "version";

    /// <summary>用户权限缓存 TTL：L1 2 分钟（降低跨节点权限变更可见延迟）/ L2 10 分钟（与原内存缓存时长一致）；仅作用于用户权限 key。</summary>
    private static readonly CacheTtlProfile PermissionTtl = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

    /// <summary>超管通配权限标识。</summary>
    public const string WildcardPermission = "*:*:*";

    /// <summary>超管角色权限字符串。</summary>
    private const string SuperAdminRoleKey = "super_admin";

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var key = await BuildKeyAsync(userId, cancellationToken);
        var cached = await cache.GetAsync<HashSet<string>>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var permissions = await LoadPermissionsAsync(userId, cancellationToken);
        await cache.SetAsync(key, permissions, PermissionTtl, cancellationToken);
        return permissions;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var permissions = await GetPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(WildcardPermission) || permissions.Contains(permission);
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(Guid userId)
    {
        await cache.RemoveAsync(BuildKeyStatic(userId, await GetVersionAsync()));
    }

    /// <inheritdoc />
    public async Task InvalidateAllAsync()
    {
        // 版本号递增后旧 key 全部失配，等效于全量失效；版本号 key 必须永不过期（Never 档），
        // 否则过期后版本号回退 0，历史 v0 用户权限 key 会复活（越权窗口）
        await cache.SetAsync(VersionCacheKey, await GetVersionAsync() + 1, CacheTtl.Never);
    }

    /// <summary>加载用户权限集合：启用角色 → 超管通配 / 角色菜单 → 菜单 perms。</summary>
    private async Task<HashSet<string>> LoadPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == userId], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)], cancellationToken: cancellationToken);
        if (roles.Count == 0)
        {
            return [];
        }

        // 超管角色：直接返回通配权限，所有权限判断放行
        if (roles.Any(r => r.RoleKey == SuperAdminRoleKey))
        {
            return [WildcardPermission];
        }

        var menuIds = (await roleMenuRepository.GetListAsync([rm => roleIds.Contains(rm.RoleId)], cancellationToken: cancellationToken))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToList();
        if (menuIds.Count == 0)
        {
            return [];
        }

        var perms = (await menuRepository.GetListAsync([m => m.Status == CommonStatus.Enabled && menuIds.Contains(m.Id)], cancellationToken: cancellationToken))
            .Where(m => !string.IsNullOrWhiteSpace(m.Perms))
            .Select(m => m.Perms!.Trim());
        return [.. perms];
    }

    /// <summary>读取当前全局版本号（未命中时默认 0）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全局版本号。</returns>
    private async Task<long> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetAsync<long>(VersionCacheKey, cancellationToken);
    }

    /// <summary>构建用户权限缓存 key（带全局版本号，全量失效后旧 key 失配）。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存 key。</returns>
    private async Task<string> BuildKeyAsync(Guid userId, CancellationToken cancellationToken)
    {
        return $"{CacheKeyPrefix}{await GetVersionAsync(cancellationToken)}:{userId}";
    }

    /// <summary>同步构建用户权限缓存 key（用于失效场景，容忍版本号读取阻塞）。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="version">当前全局版本号。</param>
    /// <returns>缓存 key。</returns>
    private string BuildKeyStatic(Guid userId, long version) => $"{CacheKeyPrefix}{version}:{userId}";
}
