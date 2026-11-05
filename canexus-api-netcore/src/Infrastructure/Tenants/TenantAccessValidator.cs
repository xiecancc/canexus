using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Tenants;

namespace Infrastructure.Tenants;
/// <summary>
/// 租户访问校验实现：按租户 Id 读取 sys_tenant（存在 / 状态 / 到期时间），经 ICache 缓存（Medium 档）。
/// <para>缓存值封装为状态记录（不缓存 null，满足 ICache 非空约束）；停用 / 到期判定以缓存快照为准，
/// 状态变更最长在 L1 1 分钟 / L2 10 分钟（Medium 档）内生效，租户领域事件监听器同时调用
/// <see cref="InvalidateAsync"/> 主动失效，使状态变更近实时生效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantRepository">租户聚合仓储。</param>
/// <param name="cache">双层缓存。</param>
public sealed class TenantAccessValidator(
    IAggregateRepository<SysTenant> tenantRepository,
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
                var tenant = await tenantRepository.GetAsync([it => it.Id == tenantId], token);
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
