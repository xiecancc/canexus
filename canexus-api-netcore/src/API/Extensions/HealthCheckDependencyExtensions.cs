
using API.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace API.Extensions;
/// <summary>
/// 健康检查注册扩展：liveness / readiness 双端点所需的检查项与框架服务。
/// <para>宿主层横切关注点，与 SecurityDependencyExtensions 同层放置；
/// 端点挂载见 <see cref="Program"/>（/health/live 与 /health/ready）。</para>
/// </summary>
public static class HealthCheckDependencyExtensions
{
    /// <summary>
    /// 注册健康检查：进程存活自检 + 三个就绪探测项（MySQL / 分布式缓存 / Quartz 调度器）。
    /// <para>检查项均带超时上限（默认 5 秒），避免依赖方挂起拖垮探针响应；
    /// 缓存检查失败为 Degraded（业务可降级），数据库与调度器失败为 Unhealthy（核心依赖）。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        var timeout = TimeSpan.FromSeconds(5);

        services.AddHealthChecks()
            // 进程存活自检：liveness 端点唯一检查项（同时计入 readiness）
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live", "ready"], timeout: timeout)
            .AddCheck<DatabaseHealthCheck>("mysql", tags: ["ready"], timeout: timeout)
            .AddCheck<CacheHealthCheck>("cache", tags: ["ready"], timeout: timeout)
            .AddCheck<SchedulerHealthCheck>("quartz", tags: ["ready"], timeout: timeout);

        return services;
    }
}
