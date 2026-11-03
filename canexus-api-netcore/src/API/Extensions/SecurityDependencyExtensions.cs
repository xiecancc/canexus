
using Domain.Shared.Json;
using Domain.Shared.Results;
using Infrastructure.Shared.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Threading.RateLimiting;

namespace API.Extensions;
/// <summary>
/// 安全传输层的依赖注册与管道扩展（CORS / 接口限流 / HTTPS 强制）：
/// <list type="bullet">
/// <item>CORS：<see cref="AddApiCors"/> 注册命名策略 + <see cref="UseApiCors"/> 挂载中间件（未启用时不挂载）；</item>
/// <item>接口限流：<see cref="AddApiRateLimiting"/> 注册 .NET 内置 RateLimiter（全局限流 + auth 端点策略）
/// + <see cref="UseApiRateLimiter"/> 挂载中间件，被限流请求返回统一 Result 结构（HTTP 429 + CommonTooManyRequests）；</item>
/// <item>HTTPS：<see cref="UseApiHttps"/>（HSTS 仅非 Development 环境生效 + 可选强制重定向）。</item>
/// </list>
/// 配置取自 <see cref="CorsOption"/> / <see cref="RateLimitOption"/> / <see cref="HttpsOption"/>（由启动时的 RegisterOptions 统一注册）。
/// </summary>
public static class SecurityDependencyExtensions
{
    /// <summary>CORS 命名策略名。</summary>
    public const string CorsPolicyName = "api-cors";

    /// <summary>认证端点限流策略名（作用于验证码 / 登录 / 刷新令牌等匿名端点）。</summary>
    public const string AuthRateLimitPolicy = "auth";

    #region 依赖注册

    /// <summary>
    /// 注册 HTTPS 安全传输参数：重定向端口与 HSTS max-age 经 Options 管道从 <see cref="HttpsOption"/> 注入
    /// （两参数均不支持热加载，首次解析定型）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiHttps(this IServiceCollection services)
    {
        services.AddOptions<HttpsRedirectionOptions>()
            .Configure<IOptionsMonitor<HttpsOption>>((https, monitor) =>
                https.HttpsPort = monitor.CurrentValue.HttpsPort > 0 ? monitor.CurrentValue.HttpsPort : null);

        services.AddOptions<HstsOptions>()
            .Configure<IOptionsMonitor<HttpsOption>>((hsts, monitor) =>
                hsts.MaxAge = TimeSpan.FromDays(monitor.CurrentValue.HstsMaxAgeDays));

        return services;
    }

    /// <summary>注册 CORS 命名策略：来源白名单 + 请求头 / 方法白名单 + AllowCredentials（策略内容随配置注入，CORS 策略不支持热加载）。
    /// <para>未启用（CorsOption.Enabled=false）时不注册策略，<see cref="UseApiCors"/> 亦不挂载中间件。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiCors(this IServiceCollection services)
    {
        services.AddOptions<CorsOptions>()
            .Configure<IOptionsMonitor<CorsOption>>((cors, monitor) =>
            {
                var option = monitor.CurrentValue;
                if (!option.Enabled)
                {
                    return;
                }

                cors.AddPolicy(CorsPolicyName, policy => policy
                    .WithOrigins([.. option.AllowedOrigins])
                    .WithHeaders([.. SplitList(option.AllowedHeaders)])
                    .WithMethods([.. SplitList(option.AllowedMethods)])
                    .SetPreflightMaxAge(TimeSpan.FromSeconds(option.PreflightMaxAgeSeconds))
                    .AllowCredentials());
            });

        return services;
    }

    /// <summary>
    /// 注册 .NET 内置接口限流（Microsoft.AspNetCore.RateLimiting）：
    /// <list type="bullet">
    /// <item>全局限流（GlobalLimiter）：按客户端 IP 分区的滑动窗口，GlobalPermitLimit=0 时关闭；</item>
    /// <item>认证端点策略（<see cref="AuthRateLimitPolicy"/>）：按客户端 IP 分区的固定窗口，
    /// 由端点经 [EnableRateLimiting("auth")] 显式引用（验证码 / 登录 / 刷新令牌）；</item>
    /// <item>被限流响应：HTTP 429 + 统一 Result 结构（CommonTooManyRequests），并附带 Retry-After 头（租约元数据提供时）。</item>
    /// </list>
    /// <para>注意：分区键取 RemoteIpAddress，部署在反向代理之后需启用 ForwardedHeaders 并配置 KnownProxies，
    /// 否则所有客户端将共享代理 IP 的限流配额。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter();

        // 限流参数从 RateLimitOption（IOptionsMonitor）注入，经 Options 管道在首次解析时定型（限流参数不支持热加载）
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptionsMonitor<RateLimitOption>>((limiter, monitor) =>
            {
                var option = monitor.CurrentValue;
                if (!option.Enabled)
                {
                    return;
                }

                // 被限流响应：与全局异常 / 401 挑战一致的 { code, message, data } 结构
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.OnRejected = static async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json;charset=utf-8";

                    // 租约元数据提供 Retry-After 时透传给客户端（fixed/sliding window 均会携带）
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter = new StringValues(((int)retryAfter.TotalSeconds).ToString());
                    }

                    await context.HttpContext.Response.WriteAsync(
                        JsonHelper.Serialize(Result.Fail(ErrorCodes.CommonTooManyRequests, "请求过于频繁，请稍后重试")), cancellationToken);
                };

                // 认证端点策略：按客户端 IP 分区的固定窗口
                limiter.AddPolicy(AuthRateLimitPolicy, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = option.AuthPermitLimit,
                            Window = TimeSpan.FromSeconds(option.AuthWindowSeconds)
                        }));

                // 全局限流：GlobalPermitLimit=0 时关闭
                if (option.GlobalPermitLimit > 0)
                {
                    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                        RateLimitPartition.GetSlidingWindowLimiter(
                            GetClientPartitionKey(httpContext),
                            _ => new SlidingWindowRateLimiterOptions
                            {
                                PermitLimit = option.GlobalPermitLimit,
                                Window = TimeSpan.FromSeconds(option.GlobalWindowSeconds),
                                SegmentsPerWindow = 4
                            }));
                }
            });

        return services;
    }

    #endregion

    #region 管道挂载

    /// <summary>挂载 HTTPS 安全传输管道：HSTS（仅非 Development 环境生效，HstsEnabled 开启时）+ 强制重定向（RedirectEnabled 开启时）。</summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>应用构建器。</returns>
    public static IApplicationBuilder UseApiHttps(this IApplicationBuilder app)
    {
        var option = app.ApplicationServices.GetRequiredService<IOptionsMonitor<HttpsOption>>().CurrentValue;
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        if (!environment.IsDevelopment() && option.HstsEnabled)
        {
            app.UseHsts();
        }

        if (option.RedirectEnabled)
        {
            app.UseHttpsRedirection();
        }

        return app;
    }

    /// <summary>挂载 CORS 中间件（CorsOption.Enabled=false 时不挂载；须注册在鉴权之前以正确处理预检请求）。</summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>应用构建器。</returns>
    public static IApplicationBuilder UseApiCors(this IApplicationBuilder app)
    {
        var option = app.ApplicationServices.GetRequiredService<IOptionsMonitor<CorsOption>>().CurrentValue;
        if (option.Enabled)
        {
            app.UseCors(CorsPolicyName);
        }

        return app;
    }

    /// <summary>挂载限流中间件（RateLimitOption.Enabled=false 时不挂载）。</summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>应用构建器。</returns>
    public static IApplicationBuilder UseApiRateLimiter(this IApplicationBuilder app)
    {
        var option = app.ApplicationServices.GetRequiredService<IOptionsMonitor<RateLimitOption>>().CurrentValue;
        if (option.Enabled)
        {
            app.UseRateLimiter();
        }

        return app;
    }

    #endregion

    /// <summary>获取限流分区键：客户端 IP（无 RemoteIpAddress 时回退为常量，仅见于测试宿主等极端场景）。</summary>
    /// <param name="httpContext">HTTP 上下文。</param>
    /// <returns>分区键。</returns>
    private static string GetClientPartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>拆分逗号分隔的白名单字符串（去空白、去空项）。</summary>
    /// <param name="value">逗号分隔字符串。</param>
    /// <returns>拆分结果。</returns>
    private static string[] SplitList(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
