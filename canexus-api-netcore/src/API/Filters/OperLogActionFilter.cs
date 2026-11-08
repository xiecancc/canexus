using System.Diagnostics;
using API.Logging;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Json;
using Domain.Shared.Logging;
using Infrastructure.Shared.Contexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Filters;
/// <summary>
/// 全局操作日志过滤器（异步 ActionFilter）：解析端点上的 <see cref="OperLogAttribute"/>，
/// 记录请求参数（敏感字段脱敏）、成功失败与异常消息、耗时、IP、操作人与链路 TraceId，写入 sys_oper_log。
/// <list type="bullet">
/// <item>无特性 → 直接放行不记录（写命令审计由 AuditLogBehavior 默认兜底）；</item>
/// <item>带特性 → 在 MediatR 管道执行前标记接管（IOperationContext.MarkOperLogHandled），
/// 管道默认审计据此跳过本请求，避免同一操作双份记录；</item>
/// <item>业务异常（含 <see cref="OperationCanceledException"/>）一律原样向上传播，
/// 由 ActionExecutedContext.Exception 或 next() 抛出路径记录失败状态，绝不吞掉改写到统一响应；</item>
/// <item>日志序列化 / 写入自身失败仅告警，不影响主流程响应。</item>
/// </list>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="operationContext">请求上下文（TraceId 读取 + 接管标记写入）。</param>
/// <param name="logger">日志。</param>
public sealed class OperLogActionFilter(
    IOperLogRepository operLogRepository,
    IUserContext currentUser,
    IOperationContext operationContext,
    ILogger<OperLogActionFilter> logger) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<OperLogAttribute>();
        if (attribute is null)
        {
            await next();
            return;
        }

        // 管道执行前标记接管：AuditLogBehavior（默认审计）据此跳过本请求，避免与过滤器重复记录
        operationContext.MarkOperLogHandled();

        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext executedContext;
        try
        {
            executedContext = await next();
        }
        catch (Exception exception)
        {
            // next() 直接抛出的异常必须原样传播到全局异常中间件；日志写入只做尽力而为的记录，
            // 不再把业务异常误当成「日志写入失败」告警吞掉
            stopwatch.Stop();
            await TryWriteLogAsync(context, attribute, stopwatch.ElapsedMilliseconds, exception);
            throw;
        }

        stopwatch.Stop();
        // MVC 常规异常路径：异常挂在 ActionExecutedContext.Exception 上，由 MVC 在过滤器链结束后重新抛出，
        // 此处仅记录失败状态，不清空 Exception（清空会吞异常）
        await TryWriteLogAsync(context, attribute, stopwatch.ElapsedMilliseconds, executedContext.Exception);
    }

    /// <summary>尽力而为写入一条操作日志；序列化 / 落库失败仅告警，绝不影响业务响应。</summary>
    /// <param name="context">执行中的 Action 上下文。</param>
    /// <param name="attribute">端点操作日志特性。</param>
    /// <param name="costMs">业务耗时（毫秒）。</param>
    /// <param name="exception">业务异常（null 表示成功）。</param>
    private async Task TryWriteLogAsync(
        ActionExecutingContext context, OperLogAttribute attribute, long costMs, Exception? exception)
    {
        try
        {
            var request = context.HttpContext.Request;
            var actionName = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName
                             ?? context.ActionDescriptor.DisplayName
                             ?? string.Empty;
            // 参数序列化 + 脱敏 + 截断统一在 try 内：任一环节失败仅放弃本条操作日志，不影响业务响应。
            // 框架注入的 CancellationToken 不是业务参数，且其 WaitHandle.Handle 为 IntPtr 不可序列化，
            // 必须从 ActionArguments 中剔除，否则全站 [OperLog] 端点都会因序列化异常丢失审计记录。
            var arguments = context.ActionArguments
                .Where(pair => pair.Value is not CancellationToken)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var operParam = OperLogSanitizer.Truncate(
                OperLogSanitizer.MaskSensitiveFields(JsonHelper.Serialize(arguments)));
            await operLogRepository.InsertAsync(new SysOperLog
            {
                Title = attribute.Module,
                BusinessType = attribute.BusinessType,
                Method = $"{context.Controller.GetType().FullName}.{actionName}",
                RequestMethod = request.Method,
                // QueryString 中的密码 / 令牌 / 证件号参数值脱敏后再截断，避免明文落到 RequestUrl 列
                RequestUrl = TruncateUrl(SensitiveDataMasker.MaskQueryString(
                    $"{request.Path}{request.QueryString}")),
                OperParam = operParam,
                OperIp = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                OperUserName = currentUser.UserName ?? string.Empty,
                UserId = currentUser.UserId,
                TraceId = operationContext.TraceId,
                Status = exception is null
                    ? Domain.Shared.Enums.ExecutionStatus.Success
                    : Domain.Shared.Enums.ExecutionStatus.Failure,
                // 异常消息可能含 SQL 参数 / 请求片段，先经共享内核脱敏再截断
                ErrorMsg = exception is null
                    ? null
                    : OperLogSanitizer.Truncate(SensitiveDataMasker.MaskFreeText(exception.Message)),
                CostMs = costMs,
                OperTime = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // 日志写入失败不影响主流程：仅记录告警
            logger.LogWarning(ex, "写入操作日志失败：{Module}", attribute.Module);
        }
    }

    /// <summary>请求 URL 截断（列长度 255，含协议头时按字符上限裁剪）。</summary>
    private static string TruncateUrl(string url) => url.Length <= 255 ? url : url[..255];
}
