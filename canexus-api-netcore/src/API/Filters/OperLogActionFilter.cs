
using System.Diagnostics;
using API.Logging;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Filters;
/// <summary>
/// 全局操作日志过滤器（异步 ActionFilter）：解析端点上的 <see cref="OperLogAttribute"/>，
/// 记录请求参数（敏感字段脱敏）、成功失败与异常消息、耗时、IP 与操作人，写入 sys_oper_log。
/// <list type="bullet">
/// <item>无特性 → 直接放行不记录；</item>
/// <item>异常（含业务异常）→ 记录 Status=0 与错误消息，异常继续向上抛由全局中间件统一响应；</item>
/// <item>日志写入自身失败仅告警，不影响主流程响应。</item>
/// </list>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="logger">日志。</param>
public sealed class OperLogActionFilter(IOperLogRepository operLogRepository, IUserContext currentUser, ILogger<OperLogActionFilter> logger) : IAsyncActionFilter
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

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var executedContext = await next();
            stopwatch.Stop();

            var request = context.HttpContext.Request;
            var exception = executedContext.Exception;
            var actionName = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName
                             ?? context.ActionDescriptor.DisplayName
                             ?? string.Empty;
            // 参数序列化 + 脱敏 + 截断统一在 try 内：任一环节失败仅放弃本条操作日志，不影响业务响应
            var operParam = OperLogSanitizer.Truncate(
                OperLogSanitizer.MaskSensitiveFields(JsonHelper.Serialize(context.ActionArguments)));
            await operLogRepository.InsertAsync(new SysOperLog
            {
                Title = attribute.Module,
                BusinessType = attribute.BusinessType,
                Method = $"{context.Controller.GetType().FullName}.{actionName}",
                RequestMethod = request.Method,
                RequestUrl = TruncateUrl($"{request.Path}{request.QueryString}"),
                OperParam = operParam,
                OperIp = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                OperUserName = currentUser.UserName ?? string.Empty,
                Status = exception is null
                    ? Domain.Shared.Enums.ExecutionStatus.Success
                    : Domain.Shared.Enums.ExecutionStatus.Failure,
                ErrorMsg = exception is null ? null : OperLogSanitizer.Truncate(exception.Message),
                CostMs = stopwatch.ElapsedMilliseconds,
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
