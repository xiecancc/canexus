
using Infrastructure.Shared.Tenants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API.Middlewares;
/// <summary>
/// 租户解析中间件：多租户开关（配置节 MultiTenancy:Enabled，默认 false）启用时，
/// 从请求头 <see cref="TenantHeader"/> 读取租户标识（非空才设置）写入 <see cref="ITenantContext"/>，
/// 由 SqlSugar 全局过滤器按租户隔离查询；禁用时完全不读取该头。
/// <para>必须注册在鉴权中间件之前；请求日志仅记录方法 / 路径 / 状态码 / 耗时，不打印租户值等请求头信息。</para>
/// </summary>
/// <remarks>构造函数（由请求管道注入）。</remarks>
/// <param name="next">管道中的下一个委托。</param>
/// <param name="tenantContext">租户上下文（AsyncLocal，请求间天然隔离）。</param>
/// <param name="logger">日志器（记录非法租户标识请求头）。</param>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    ITenantContext tenantContext,
    ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>租户标识请求头名称。</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>中间件入口：按开关解析租户标识后继续管道。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="configuration">应用配置（读取多租户开关）。</param>
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (configuration.GetValue("MultiTenancy:Enabled", false))
        {
            var tenantId = context.Request.Headers.TryGetValue(TenantHeader, out var values)
                ? values.FirstOrDefault()
                : null;
            if (Guid.TryParse(tenantId, out var tenantGuid) && tenantGuid != Guid.Empty)
            {
                tenantContext.TenantId = tenantGuid;
            }
            else if (!string.IsNullOrWhiteSpace(tenantId))
            {
                logger.LogWarning("请求头 {HeaderName} 的值不是有效的租户标识，已忽略", TenantHeader);
            }
        }

        await next(context);
    }
}
