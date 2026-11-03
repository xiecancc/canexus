
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace API.HealthChecks;
/// <summary>
/// 分布式缓存健康检查：向 L2 缓存（IDistributedCache，RedisEnabled=true 时为 Redis）写入探针值并回读校验。
/// <para>失败为 Degraded 而非 Unhealthy：缓存层本身容错降级（ICache 读写失败不阻断业务），
/// L2 不可用时应用仍可服务，仅跨节点一致性时窗变大。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">分布式缓存（单例工厂注册：Redis 或进程内存实现）。</param>
/// <param name="cacheOption">缓存配置（用于注明当前 L2 形态）。</param>
public sealed class CacheHealthCheck(IDistributedCache cache, IOptionsMonitor<CacheOption> cacheOption) : IHealthCheck
{
    /// <summary>探针缓存 key（短 TTL 自清理）。</summary>
    private const string ProbeKey = "health:probe";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetAsync(ProbeKey, "ping"u8.ToArray(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
                cancellationToken);
            var echoed = await cache.GetAsync(ProbeKey, cancellationToken);
            return echoed is not null
                ? HealthCheckResult.Healthy($"分布式缓存正常（L2：{(cacheOption.CurrentValue.RedisEnabled ? "Redis" : "内存")}）")
                : HealthCheckResult.Degraded("分布式缓存探针回读失败");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded("分布式缓存不可用（业务已自动降级为纯 L1）", exception);
        }
    }
}
