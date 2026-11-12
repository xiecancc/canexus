
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Constants;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Locking;
using Microsoft.Extensions.Logging;

namespace Application.Permissions;
/// <summary>
/// 权限校验器实现：双层缓存按 userId 缓存权限集合（L1 2 分钟 / L2 10 分钟）。
/// <para>全局版本号存于缓存（<see cref="VersionCacheKey"/>，永不过期），全量失效时递增，旧 key 自然失配，无需遍历缓存。</para>
/// <para>版本号 key 若丢失（Redis 重启 / flush / 淘汰 / 首次冷启动），<c>GetVersionAsync</c> 会重建一个
/// 大于任何历史版本号的安全单调值并写回，绝不回退 0，从根本上消除历史 v0 用户权限 key 复活的越权窗口。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="userRoleRepository">用户角色关联仓储。</param>
/// <param name="roleRepository">角色仓储。</param>
/// <param name="roleMenuRepository">角色菜单关联仓储。</param>
/// <param name="menuRepository">菜单仓储。</param>
/// <param name="cache">双层缓存端口。</param>
/// <param name="distributedLock">分布式锁端口。</param>
/// <param name="logger">日志器。</param>
public sealed class PermissionChecker(
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainRepository<SysMenu> menuRepository,
    ICache cache,
    IDistributedLock distributedLock,
    ILogger<PermissionChecker> logger) : IPermissionChecker
{
    /// <summary>缓存 key 前缀（key 格式：{prefix}{version}:{userId}）。</summary>
    public const string CacheKeyPrefix = "auth:permissions:";

    /// <summary>全局版本号缓存 key（永不过期档；丢失后由 <c>GetVersionAsync</c> 重建安全版本号，不再回退 0）。</summary>
    private const string VersionCacheKey = CacheKeyPrefix + "version";

    /// <summary>用户权限缓存 TTL：L1 2 分钟（降低跨节点权限变更可见延迟）/ L2 10 分钟（与原内存缓存时长一致）；仅作用于用户权限 key。</summary>
    private static readonly CacheTtlProfile PermissionTtl = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

    /// <summary>超管通配权限标识。</summary>
    public const string WildcardPermission = "*:*:*";

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
    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await cache.RemoveAsync(BuildKeyStatic(userId, await GetVersionAsync(cancellationToken)), cancellationToken);
    }

    /// <summary>权限版本号锁键（保护版本号读-改-写的原子性）。</summary>
    private const string VersionLockKey = CacheKeyPrefix + "version-lock";

    /// <inheritdoc />
    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        // 版本号递增后旧 key 全部失配，等效于全量失效；即使版本号 key 丢失（过期 / 重启 / flush / 淘汰），
        // GetVersionAsync 也会重建一个大于任何历史版本号的安全单调值，历史 v0 用户权限 key 永不复活。
        // 若本次递增恰逢版本号缺失，锁内 GetVersionAsync 先重建安全版本号 V，随后本次写入 V+1，
        // 仍是一次等价自增，不会重复触发递增导致读-改-写混乱。
        // 递增经分布式锁保护：多实例并发失效时读-改-写不丢自增；未获取到锁（等待超时 / Redis 故障）
        // 降级为无锁递增，最坏丢失一次自增，由用户权限 key 的 TTL（10 分钟）兜底最终一致
        await using var handle = await distributedLock.AcquireAsync(
            VersionLockKey, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), cancellationToken);
        await cache.SetAsync(VersionCacheKey, await GetVersionAsync(cancellationToken) + 1, CacheTtl.Never, cancellationToken);
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
        if (roles.Any(r => r.RoleKey == RoleConstants.SuperAdminRoleKey))
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

    /// <summary>进程内观测到的最大版本号（保证重建值在系统时钟回拨时仍严格单调递增）。</summary>
    private static long _lastVersion;

    /// <summary>保护 <c>_lastVersion</c> 读-改-写的进程级锁。</summary>
    private static readonly object VersionSync = new();

    /// <summary>读取当前全局版本号；缓存缺失时重建安全单调版本号并写回，绝不回退 0。</summary>
    /// <remarks>
    /// 缓存读取端口未命中（或读取降级）返回 <c>default(long) == 0</c>。旧实现直接返回 0：
    /// Redis 重启 / flush / 淘汰后版本号回退 0，历史 <c>v0</c> 用户权限 key 会复活，形成越权窗口。
    /// 本实现把 0 视为「版本号丢失」，重建一个以毫秒时间戳为基线、进程内严格单调的安全版本号
    /// （恒大于任何历史小整数版本号），以 <c>CacheTtl.Never</c> 写回并告警。
    /// 首次冷启动本就是 miss，生成大版本号同样安全——所有用户权限 key 自该版本起生效。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全局版本号（恒为正的大版本号，绝不为 0）。</returns>
    private async Task<long> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var version = await cache.GetAsync<long>(VersionCacheKey, cancellationToken);
        if (version != 0)
        {
            TrackVersion(version);
            return version;
        }

        var safeVersion = NextSafeVersion();
        await cache.SetAsync(VersionCacheKey, safeVersion, CacheTtl.Never, cancellationToken);
        logger.LogWarning(
            "权限全局版本号缓存缺失（Redis 重启 / flush / 淘汰或首次冷启动），已重建安全版本号 {Version}；历史版本（含 v0）用户权限缓存自动失效",
            safeVersion);
        return safeVersion;
    }

    /// <summary>把从缓存读到的版本号纳入单调基线，避免后续重建值回退到该版本或更早版本。</summary>
    /// <param name="version">缓存中的当前全局版本号。</param>
    private static void TrackVersion(long version)
    {
        lock (VersionSync)
        {
            if (version > _lastVersion)
            {
                _lastVersion = version;
            }
        }
    }

    /// <summary>生成安全版本号：毫秒时间戳与「进程内最大版本号 + 1」取较大者，兼容时钟回拨与同毫秒并发重建。</summary>
    /// <returns>严格大于任何进程内已观测版本号的安全版本号。</returns>
    private static long NextSafeVersion()
    {
        lock (VersionSync)
        {
            _lastVersion = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastVersion + 1);
            return _lastVersion;
        }
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
