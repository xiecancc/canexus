using Domain.Shared.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API.Middlewares;
/// <summary>
/// 请求体大小上限中间件（P0-S3 上传上限对齐的测试宿主兼容层）：
/// 对声明了 Content-Length 且超过上限（FileStorage:MaxSizeMB + 1MB，与 Kestrel 线级上限一致）的请求，
/// 直接以 413 + 401203 拒绝，避免大载荷进入业务校验链路。
/// <para>真实 Kestrel 宿主由 ConfigureKestrel(MaxRequestBodySize) 线级拒绝（BadHttpRequestException→413 分支）；
/// 本中间件保证 TestServer（WebApplicationFactory 测试宿主不强制 Kestrel 上限）行为一致，且对分块传输
/// （无 Content-Length）的请求不构成额外负担。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="next">管道中的下一个委托。</param>
/// <param name="logger">日志器（记录 413 拒绝事件）。</param>
public sealed class RequestBodyLimitMiddleware(
    RequestDelegate next,
    ILogger<RequestBodyLimitMiddleware> logger)
{
    /// <summary>中间件入口：按 Content-Length 与配置上限决定拒绝或放行。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="configuration">应用配置（读取 FileStorage:MaxSizeMB）。</param>
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        var maxUploadMb = configuration.GetValue("FileStorage:MaxSizeMB", 10);
        var limitBytes = (maxUploadMb + 1) * 1024L * 1024;

        if (context.Request.ContentLength is not null && context.Request.ContentLength > limitBytes)
        {
            logger.LogWarning("请求体大小拒绝：Content-Length {Length} 超上限 {Limit}（路径 {Path}）",
                context.Request.ContentLength, limitBytes, context.Request.Path);
            await UnifiedResponseWriter.WriteResultAsync(
                context, StatusCodes.Status413PayloadTooLarge,
                Result.Fail(ErrorCodes.FileSizeExceeded, "上传文件大小超出上限"));
            return;
        }

        await next(context);
    }
}
