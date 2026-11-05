
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;
/// <summary>
/// 性能监控管道行为：统计每个请求的处理耗时，超过阈值（500ms）输出告警日志。
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>慢请求阈值（毫秒）。</summary>
    private const long SlowRequestThresholdMs = 500;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // 计时从 Handle 入口开始（局部变量）：字段初始化计时会在管道构建期即启动，
        // 计入验证 / 日志等前序行为的执行开销，且异常路径不会停表
        var timer = Stopwatch.StartNew();

        var response = await next();

        timer.Stop();
        var elapsedMs = timer.ElapsedMilliseconds;
        if (elapsedMs > SlowRequestThresholdMs)
        {
            var requestName = typeof(TRequest).Name;
            // TraceId（P2-B3）：关联请求链路标识——经响应头 X-Trace-Id 取值即可在日志中一键
            // 定位同一请求的全部 SQL / 事件投递日志（无请求上下文时输出占位符）
            var traceId = Activity.Current?.TraceId.ToString() ?? "-";
            logger.LogWarning("慢请求：{RequestName} 处理耗时 {ElapsedMs}ms（阈值 {ThresholdMs}ms）TraceId={TraceId}",
                requestName, elapsedMs, SlowRequestThresholdMs, traceId);
        }

        return response;
    }
}
