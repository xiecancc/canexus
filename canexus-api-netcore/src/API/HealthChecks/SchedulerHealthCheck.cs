
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace API.HealthChecks;
/// <summary>
/// 调度器健康检查：验证 Quartz 调度器已创建且处于启动状态（应用启动即 Start，未启动属异常形态）。
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="schedulerFactory">Quartz 调度器工厂（AddQuartz 注册）。</param>
public sealed class SchedulerHealthCheck(ISchedulerFactory schedulerFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            return scheduler is { IsStarted: true }
                ? HealthCheckResult.Healthy("Quartz 调度器运行中")
                : HealthCheckResult.Unhealthy("Quartz 调度器未启动");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Quartz 调度器状态查询失败", exception);
        }
    }
}
