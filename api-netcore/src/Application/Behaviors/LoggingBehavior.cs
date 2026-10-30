namespace Application.Behaviors;

using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// 日志管道行为：记录每个请求的开始 / 完成与未处理异常（含耗时），便于排查分发链路问题。
/// <para>不记录请求负载数据（可能包含密码等敏感信息），仅记录请求类型名称。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("处理请求 {RequestName} 开始", requestName);
        try
        {
            var response = await next();
            logger.LogInformation("处理请求 {RequestName} 完成", requestName);
            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "处理请求 {RequestName} 异常", requestName);
            throw;
        }
    }
}
