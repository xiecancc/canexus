using System.Diagnostics;

namespace API.Middlewares;

/// <summary>
/// TraceId 中间件（P2-B3）：为每个请求建立 OpenTelemetry 兼容的 Activity（ActivitySource 打点），
/// 生成 32 位十六进制 TraceId 作为请求链路标识，写入 HttpContext.TraceIdentifier 并随响应头
/// X-Trace-Id 返回。
/// <para>同请求内 MediatR 管道（慢请求日志）、SqlSugar Aop（SQL 日志）与事件投递日志共享
/// Activity.Current 的同一 TraceId——慢接口可经响应头一键定位到 SQL / 事件链路。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="next">后续中间件。</param>
public sealed class TraceIdMiddleware(RequestDelegate next)
{
    /// <summary>API 请求链路 ActivitySource（后续可接入 OTLP 导出，改动局部）。</summary>
    private static readonly ActivitySource Source = new("SuperAdmin.Api");

    /// <summary>
    /// 采样监听器：无外部导出器（OTLP 未接入）时仍创建 Activity 并采样，
    /// 保证 TraceId 生成与 SQL / 事件日志关联不依赖第三方监听器。
    /// </summary>
    private static readonly ActivityListener Listener = new()
    {
        ShouldListenTo = source => source.Name == Source.Name,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };

    /// <summary>注册采样监听器（静态初始化一次，进程内全局生效）。</summary>
    static TraceIdMiddleware()
    {
        ActivitySource.AddActivityListener(Listener);
    }

    /// <summary>为请求生成 TraceId 并贯穿后续管道。</summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        using var activity = Source.StartActivity("api.request", ActivityKind.Server);

        // Activity 不可用（打点关闭）时回退随机 32 位十六进制，保证响应头恒有链路标识
        var traceId = activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = traceId;
        context.Response.Headers["X-Trace-Id"] = traceId;

        await next(context);
    }
}
