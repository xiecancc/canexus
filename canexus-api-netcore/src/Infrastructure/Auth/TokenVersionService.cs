
using Domain.Entities;
using Domain.Services.Auth;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Infrastructure.Auth;
/// <summary>
/// 令牌版本服务实现：用户级版本持久化在 sys_user.TokenVersion，请求期读取经 ICache 双层缓存（L1 内存 + L2 Redis），
/// 正常情况下单次请求只读进程内存，不回源数据库；版本递增后清除缓存并按 Redis 广播失效其他实例。
/// <para>会话级吊销使用缓存集合（key 含 sid），TTL 覆盖 accessToken 生命周期 + 5 分钟安全余量；
/// Redis 不可用时降级为进程内缓存，单副本部署行为一致，多副本部署需要 Redis 才能即时跨实例生效。</para>
/// <para>吊销状态为「单向标记」：只有被吊销的会话才写入 true，未吊销状态不进缓存。
/// 因此不存在「其他实例 L1 缓存 false → 登出后仍放行至 TTL 过期」的窗口，
/// 也不依赖 Pub/Sub 广播的可靠性；多副本下由 L2 统一视图保证下一次请求即读到 true。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（版本递增使用独立连接，避免受调用方事务状态影响）。</param>
/// <param name="cache">应用缓存。</param>
/// <param name="options">JWT 配置监视器（读取 accessToken 有效期以确定会话吊销 TTL）。</param>
/// <param name="logger">日志器（记录平台级全表操作的结构化审计）。</param>
public sealed class TokenVersionService(
    ISqlSugarClient client,
    ICache cache,
    IOptionsMonitor<JwtOption> options,
    ILogger<TokenVersionService> logger) : ITokenVersionService
{
    /// <summary>用户令牌版本缓存 key 前缀（key 格式：{prefix}{userId}）。</summary>
    public const string VersionCacheKeyPrefix = "auth:token-version:";

    /// <summary>会话吊销缓存 key 前缀（key 格式：{prefix}{sessionId}）。</summary>
    public const string SessionRevokeCacheKeyPrefix = "auth:session-revoked:";

    /// <inheritdoc />
    public async Task<TokenVersionState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var state = await cache.GetOrCreateAsync(
            VersionCacheKeyPrefix + userId,
            async token =>
            {
                // 清空过滤器：停用 / 软删除用户仍需返回其版本与可用性，交给校验方统一判定
                var user = await client.Queryable<SysUser>()
                    .ClearFilter<SysUser>()
                    .Where(u => u.Id == userId)
                    .FirstAsync(token);
                return user is null
                    ? TokenVersionState.Missing
                    : new TokenVersionState(
                        user.TokenVersion,
                        user.Status == Domain.Shared.Enums.CommonStatus.Enabled && user.DeletedAt == null);
            },
            CacheTtl.Medium,
            cancellationToken);

        return state.Version < 0 ? null : state;
    }

    /// <inheritdoc />
    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
        => RevokeAllForUsersAsync([userId], cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAllForUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        // 平台级跨租户全表操作：已登记在 PlatformWideOperationWhitelist（TokenVersionRevokeAll），刻意不补租户条件。
        // 单条 UPDATE 原子递增，避免并发读-改-写丢失版本；独立连接保证事件在业务事务提交后仍可执行
        var affectedRows = await client.CopyNew()
            .Updateable<SysUser>()
            .SetColumns(u => u.TokenVersion == u.TokenVersion + 1)
            .Where(u => ids.Contains(u.Id))
            .ExecuteCommandAsync(cancellationToken);

        logger.LogInformation(
            "[平台级全表操作][白名单] Operation={Operation} 目标用户数={TargetCount} 影响行数={AffectedRows}",
            PlatformWideOperationWhitelist.TokenVersionRevokeAll, ids.Count, affectedRows);

        // 逐 key 清除：RemoveAsync 同时清除本实例 L1 / L2，并在 Redis 可用时广播其他实例清除 L1
        await Task.WhenAll(ids.Select(id => cache.RemoveAsync(VersionCacheKeyPrefix + id, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var ttl = TimeSpan.FromMinutes(options.CurrentValue.AccessTokenMinutes + 5);

        // 只写吊销标记（true）：未吊销状态不缓存，故无需「先删后写」清掉其他实例的 false，
        // 也不存在删除与写入之间并发请求回源写回 false 的竞态。L2 为跨实例的权威视图。
        await cache.SetAsync(
            SessionRevokeCacheKeyPrefix + sessionId,
            true,
            new CacheTtlProfile(TimeSpan.FromMinutes(1), ttl),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsSessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return false;
        }

        // 只读不回源（ICache.GetAsync 已用 HybridCache flags 禁止写入 factory 结果）：
        // 缓存中不存在即「未吊销」，不会把 false 固化在 L1 / L2 里。
        return await cache.GetAsync<bool>(SessionRevokeCacheKeyPrefix + sessionId, cancellationToken);
    }
}
