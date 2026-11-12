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

    /// <summary>
    /// 跨切响应头白名单：这些头由 CORS / HSTS 等中间件在管道内层设置，
    /// <c>Response.Clear()</c> 会把它们连同内层已写入的响应体一并清空，导致跨域 401/403/429/500
    /// 缺少 <c>Access-Control-Allow-Origin</c> / <c>Strict-Transport-Security</c>，前端读不到错误体。
    /// 写入统一响应前先暂存、Clear 后恢复，保证「统一结果结构」与「跨切安全头」同时成立。
    /// </summary>
    private static readonly string[] PreservedHeaderPrefixes = ["Access-Control-", "Strict-Transport-Security", "Vary", "X-Trace-Id"];

    /// <summary>将统一响应结果写入 HTTP 响应（响应已开始时放弃写入，避免破坏响应流）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="result">统一响应。</param>
    /// <param name="headers">附加响应头（如限流的 Retry-After）；在 Clear 之后写入，优先级高于保留头。</param>
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

        // 暂存跨切中间件已设置的头（CORS / HSTS / Vary / TraceId），Clear 之后逐一恢复
        List<KeyValuePair<string, string?>>? preserved = null;
        foreach (var (name, value) in context.Response.Headers)
        {
            if (Array.Exists(PreservedHeaderPrefixes, prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                (preserved ??= []).Add(new KeyValuePair<string, string?>(name, value.ToString()));
            }
        }

        context.Response.Clear();
        if (preserved is { Count: > 0 })
        {
            foreach (var (name, value) in preserved)
            {
                context.Response.Headers[name] = value;
            }
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json;charset=utf-8";
        // 无跨切头时会重新写入 TraceId；有跨切头时覆盖为本次请求的值，保证链路标识一致
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
