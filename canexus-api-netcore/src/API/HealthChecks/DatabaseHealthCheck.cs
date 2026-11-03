
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace API.HealthChecks;
/// <summary>
/// 数据库健康检查：执行 <c>SELECT 1</c> 验证 MySQL 连通性（含连接池耗尽、网络中断、鉴权失效等故障形态）。
/// <para>失败即 Unhealthy（数据库是核心依赖，不可用时应摘除流量）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（单例，与业务共用连接池）。</param>
public sealed class DatabaseHealthCheck(ISqlSugarClient client) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await client.Ado.GetStringAsync("SELECT 1");
            return HealthCheckResult.Healthy("数据库连接正常");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("数据库连接异常", exception);
        }
    }
}
