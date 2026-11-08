using Infrastructure.Shared.DataAccess;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.HealthChecks;

/// <summary>
/// 初始化就绪检查（T-13）：把「库 / 结构 / 种子」三层判定结论接入 <c>/health/ready</c>。
/// <para>任一阶段未就绪即 Unhealthy（探针返回 503）；描述文本保持通用，不含表名、连接串或异常细节
/// （健康端点匿名可达，且报告写出器不输出 description）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="state">数据库初始化结论状态。</param>
public sealed class InitializationHealthCheck(DatabaseInitializationState state) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("数据库初始化已完成")
            : HealthCheckResult.Unhealthy("数据库初始化未完成"));
    }
}
