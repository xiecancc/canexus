using Domain.Entities;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Tenants;
/// <summary>
/// 租户访问校验实现：按租户 Id 读取 sys_tenant（存在 / 状态 / 到期时间），经 ICache 缓存（Medium 档）。
/// <para>缓存值封装为状态记录（不缓存 null，满足 ICache 非空约束）；停用 / 到期判定以缓存快照为准，
/// 状态变更最长在 L1 1 分钟 / L2 10 分钟（Medium 档）内生效，租户领域事件监听器同时调用
/// <see cref="InvalidateAsync"/> 主动失效，使状态变更近实时生效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（平台级租户表需绕开租户过滤器，与 TokenVersionService 读 SysUser 同范式）。</param>
/// <param name="cache">双层缓存。</param>
public sealed class TenantAccessValidator(
    ISqlSugarClient client,
    ICache cache) : ITenantAccessValidator
{
    /// <summary>缓存键前缀。</summary>
    public const string CacheKeyPrefix = "tenant:access:";

    /// <inheritdoc />
    public async Task<TenantAccessStatus> ValidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var state = await cache.GetOrCreateAsync(
            CacheKeyPrefix + tenantId,
            async token =>
            {
                // sys_tenant 是平台级全局表（TenantId IS NULL），但 TenantResolutionMiddleware 在认证前
                // 已把 X-Tenant-Id 写入 ITenantContext；若走通用仓储，查询会被「it.TenantId == 当前租户」
                // 过滤器屏蔽，冷缓存时把存在且启用的租户误判为 NotFound（V-01 实测复现）。
                // 这里显式 ClearFilter 只作用于本查询（SqlSugar 原生 API），同时补回软删除语义，
                // 不改变全局过滤器注册机制，也不影响其他实体的租户隔离。
                var tenant = await client.Queryable<SysTenant>()
                    .ClearFilter<SysTenant>()
                    .Where(it => it.Id == tenantId && it.DeletedAt == null)
                    .FirstAsync(token);
                return new TenantCacheState(
                    tenant is not null,
                    tenant?.Status ?? CommonStatus.Disabled,
                    tenant?.ExpireTime);
            },
            CacheTtl.Medium,
            cancellationToken);

        if (!state.Exists)
        {
            return TenantAccessStatus.NotFound;
        }

        if (state.Status != CommonStatus.Enabled)
        {
            return TenantAccessStatus.Disabled;
        }

        if (state.ExpireTime is not null && state.ExpireTime.Value < DateTime.UtcNow)
        {
            return TenantAccessStatus.Expired;
        }

        return TenantAccessStatus.Ok;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(CacheKeyPrefix + tenantId, cancellationToken);

    /// <summary>缓存状态快照（存在性 + 状态 + 到期时间；Exists=false 时 Status 无意义）。</summary>
    private sealed record TenantCacheState(bool Exists, CommonStatus Status, DateTime? ExpireTime);
}
