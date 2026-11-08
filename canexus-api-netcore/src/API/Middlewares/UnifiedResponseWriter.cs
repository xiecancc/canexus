using Domain.Shared.Json;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Http;

namespace API.Middlewares;
/// <summary>
/// 统一响应写入器（全站唯一实现）：全局异常处理、鉴权挑战、限流拒绝与横切中间件
/// 全部经此输出一致的 Result 结构、Content-Type 与 X-Trace-Id 响应头。
/// </summary>
internal static class UnifiedResponseWriter
{
    /// <summary>响应头名称：请求链路 TraceId。</summary>
    private const string TraceIdHeader = "X-Trace-Id";

    /// <summary>将统一响应结果写入 HTTP 响应（响应已开始时放弃写入，避免破坏响应流）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="result">统一响应。</param>
    /// <param name="headers">附加响应头（如限流的 Retry-After）；在 Clear 之后写入，避免被清空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task WriteResultAsync(
        HttpContext context,
        int statusCode,
        Result result,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json;charset=utf-8";
        context.Response.Headers[TraceIdHeader] = context.TraceIdentifier;
        if (headers is { Count: > 0 })
        {
            foreach (var header in headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        await context.Response.WriteAsync(JsonHelper.Serialize(result), cancellationToken);
    }
}
