namespace Application.Behaviors;

using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

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

    private readonly Stopwatch _timer = Stopwatch.StartNew();

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        _timer.Stop();
        var elapsedMs = _timer.ElapsedMilliseconds;
        if (elapsedMs > SlowRequestThresholdMs)
        {
            var requestName = typeof(TRequest).Name;
            logger.LogWarning("慢请求：{RequestName} 处理耗时 {ElapsedMs}ms（阈值 {ThresholdMs}ms）",
                requestName, elapsedMs, SlowRequestThresholdMs);
        }

        return response;
    }
}
