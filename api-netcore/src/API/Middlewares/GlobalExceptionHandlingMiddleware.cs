namespace API.Middlewares;

using System.Text.Json;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>
/// 全局异常处理中间件：
/// <list type="bullet">
/// <item><see cref="BusinessException"/>：按业务错误码映射 HTTP 状态（默认 400，认证段 401、权限段 403、资源段 404），返回统一 Result；</item>
/// <item>未知异常：返回 500 + 统一 Result（固定消息“服务器内部错误”，不向客户端泄露堆栈），并以 Error 级别记录 Serilog。</item>
/// </list>
/// </summary>
public sealed class GlobalExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    /// <summary>构造函数（由请求管道注入）。</summary>
    /// <param name="next">管道中的下一个委托。</param>
    /// <param name="logger">日志记录器。</param>
    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>中间件入口：捕获下游管道的未处理异常并转换为统一响应。</summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BusinessException exception)
        {
            // 业务异常：错误码映射 HTTP 状态，消息可直接返回给客户端
            var statusCode = ErrorCodes.GetHttpStatusCode(exception.Code);
            _logger.LogWarning("业务异常：{Code} - {Message}", exception.Code, exception.Message);
            await WriteResultAsync(context, statusCode, Result.Fail(exception.Code, exception.Message));
        }
        catch (FluentValidation.ValidationException exception)
        {
            // 验证异常：由 MediatR 验证管道行为抛出，拼接全部失败项，返回 400 + 通用校验失败错误码
            var message = string.Join("；", exception.Errors.Select(error => error.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)));
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "请求参数校验失败";
            }

            _logger.LogWarning("验证异常：{Message}", message);
            await WriteResultAsync(context, StatusCodes.Status400BadRequest, Result.Fail(ErrorCodes.CommonValidationFailed, message));
        }
        catch (Exception exception)
        {
            // 未知异常：不泄露堆栈与内部信息，仅返回固定文案，完整堆栈写入日志
            _logger.LogError(exception, "未处理异常：{Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteResultAsync(
                context,
                StatusCodes.Status500InternalServerError,
                Result.Fail(ErrorCodes.CommonSystemError, "服务器内部错误"));
        }
    }

    /// <summary>将统一响应结果写入 HTTP 响应。</summary>
    private static async Task WriteResultAsync(HttpContext context, int statusCode, Result result)
    {
        // 响应已开始输出时无法改写状态码，只能中止后续写入，避免破坏响应流
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json;charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
    }
}
