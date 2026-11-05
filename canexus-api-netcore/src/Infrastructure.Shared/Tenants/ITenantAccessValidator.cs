namespace Infrastructure.Shared.Tenants;

/// <summary>
/// 租户访问校验端口：按租户 Id 判定其当前是否可用于请求（存在、启用且未到期）。
/// <para>供 API 层租户授权中间件在认证后校验请求头 X-Tenant-Id，阻止平台用户访问不存在 / 停用 / 到期租户。</para>
/// </summary>
public interface ITenantAccessValidator
{
    /// <summary>校验租户当前可用性（带缓存，租户状态变更最长 TTL 内生效）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租户访问校验结果。</returns>
    public Task<TenantAccessStatus> ValidateAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>失效指定租户的访问状态缓存（租户创建 / 修改 / 删除 / 恢复后调用，使状态变更近实时生效）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
