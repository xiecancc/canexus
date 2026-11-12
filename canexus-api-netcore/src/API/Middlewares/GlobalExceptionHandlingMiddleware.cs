
using Domain.Shared.Exceptions;
using Domain.Shared.Localization;
using Domain.Shared.Results;

namespace API.Middlewares;
/// <summary>
/// 全局异常处理中间件：
/// <list type="bullet">
/// <item><see cref="BusinessException"/>：按业务错误码映射 HTTP 状态（认证段 401、权限段 403、系统段 500、限流 429，其余默认 400；完整规则见 ErrorCodes.GetHttpStatusCode），返回统一 Result；</item>
/// <item>未知异常：返回 500 + 统一 Result（固定消息“服务器内部错误”，不向客户端泄露堆栈），并以 Error 级别记录 Serilog；</item>
/// <item>BadHttpRequestException：按 Kestrel 报告的 StatusCode 精确映射（413 上传超限 → 上传超限业务码；其余 400 类 → 通用请求错误码），不再一刀切标注 413（P1-C1）。</item>
/// <item>客户端断开（OperationCanceledException 且 RequestAborted 触发）：非服务端错误，仅 Information 记录，不写响应、不计 500。</item>
/// </list>
/// 统一响应附 <c>X-Trace-Id</c> 响应头（HttpContext.TraceIdentifier），客户端反馈该值即可在日志中精确定位对应请求。
/// </summary>
/// <remarks>构造函数（由请求管道注入）。</remarks>
/// <param name="next">管道中的下一个委托。</param>
/// <param name="logger">日志记录器。</param>
public sealed class GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    /// <summary>中间件入口：捕获下游管道的未处理异常并转换为统一响应。</summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessException exception)
        {
            // 业务异常：错误码映射 HTTP 状态；消息经本地化器按请求文化翻译
            // （未收录的错误码回退异常原始文案，渐进式收录见 Domain.Shared/Localization）
            var statusCode = ErrorCodes.GetHttpStatusCode(exception.Code);
            var message = ErrorMessageLocalizer.Localize(exception.Code, exception.Message);
            logger.LogWarning("业务异常：{Code} - {Message}（TraceId：{TraceId}）",
                exception.Code, message, context.TraceIdentifier);
            await WriteResultAsync(context, statusCode, Result.Fail(exception.Code, message));
        }
        catch (FluentValidation.ValidationException exception)
        {
            // 验证异常：由 MediatR 验证管道行为抛出，拼接全部失败项，返回 400 + 通用校验失败错误码
            var message = string.Join("；", exception.Errors.Select(error => error.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)));
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "请求参数校验失败";
            }

            logger.LogWarning("验证异常：{Message}（TraceId：{TraceId}）", message, context.TraceIdentifier);
            // 校验失败文案同样经本地化器 + 脱敏：zh 保留逐字段细节，en-US 返回资源表英文文案
            await WriteResultAsync(context, StatusCodes.Status400BadRequest,
                Result.Fail(ErrorCodes.CommonValidationFailed,
                    ErrorMessageLocalizer.Localize(ErrorCodes.CommonValidationFailed, message)));
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception)
        {
            // 按 Kestrel 报告的 StatusCode 精确区分（P1-C1）：仅 413（请求体超出 Kestrel 线级上限，
            // 如上传文件超过 FileStorage:MaxSizeMB+1MB 边界）映射为 413 + 上传超限业务码；
            // 同类型异常也承载「请求行非法 / 请求体意外结束」等 400 类问题，一律标注 413 会误导
            // 客户端重试语义（误以为是文件过大）。其余状态按原样映射为对应 HTTP 状态 + 通用请求错误码
            var statusCode = exception.StatusCode is > 0 ? exception.StatusCode : StatusCodes.Status400BadRequest;
            if (statusCode == StatusCodes.Status413PayloadTooLarge)
            {
                logger.LogWarning("请求体超出大小上限：{Reason}（TraceId：{TraceId}）", exception.Message, context.TraceIdentifier);
                await WriteResultAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    Result.Fail(ErrorCodes.FileSizeExceeded,
                        ErrorMessageLocalizer.Localize(ErrorCodes.FileSizeExceeded, "上传文件大小超出上限")));
                return;
            }

            logger.LogWarning("非法请求（BadHttpRequest，HTTP {StatusCode}）：{Reason}（TraceId：{TraceId}）",
                statusCode, exception.Message, context.TraceIdentifier);
            await WriteResultAsync(
                context,
                statusCode,
                Result.Fail(ErrorCodes.CommonInvalidRequest,
                    ErrorMessageLocalizer.Localize(ErrorCodes.CommonInvalidRequest, "请求格式非法")));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端中途断开（RequestAborted 触发）导致的取消：非服务端错误，
            // 仅记录信息级日志便于追踪，不写响应（连接已关闭，写入无意义），也不计为 500 误报
            logger.LogInformation("请求已取消（客户端断开）：{Method} {Path}（TraceId：{TraceId}）",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
        }
        catch (Exception exception)
        {
            // 未知异常：不泄露堆栈与内部信息，仅返回固定文案，完整堆栈 + TraceId 写入日志供排查
            logger.LogError(exception, "未处理异常：{Method} {Path}（TraceId：{TraceId}）",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            await WriteResultAsync(
                context,
                StatusCodes.Status500InternalServerError,
                Result.Fail(ErrorCodes.CommonSystemError,
                    ErrorMessageLocalizer.Localize(ErrorCodes.CommonSystemError, "服务器内部错误")));
        }
    }

    /// <summary>统一响应写入委托到全站唯一实现（含 X-Trace-Id 与响应已开始时的短路）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="result">统一响应。</param>
    private static Task WriteResultAsync(HttpContext context, int statusCode, Result result) =>
        UnifiedResponseWriter.WriteResultAsync(context, statusCode, result);
}
