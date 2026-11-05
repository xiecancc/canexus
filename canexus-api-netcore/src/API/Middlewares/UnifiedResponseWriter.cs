using Domain.Shared.Json;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Http;

namespace API.Middlewares;
/// <summary>统一响应写入器：供横切中间件按业务错误码输出与全局异常处理一致的 Result 结构（含 X-Trace-Id 响应头）。</summary>
internal static class UnifiedResponseWriter
{
    /// <summary>将统一响应结果写入 HTTP 响应（响应已开始时放弃写入，避免破坏响应流）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="result">统一响应。</param>
    public static async Task WriteResultAsync(HttpContext context, int statusCode, Result result)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json;charset=utf-8";
        context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
        await context.Response.WriteAsync(JsonHelper.Serialize(result));
    }
}
