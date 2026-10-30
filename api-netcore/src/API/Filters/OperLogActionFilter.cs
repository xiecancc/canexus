namespace API.Filters;

using API.Logging;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// 全局操作日志过滤器（异步 ActionFilter）：解析端点上的 <see cref="OperLogAttribute"/>，
/// 记录请求参数（敏感字段脱敏）、成功失败与异常消息、耗时、IP 与操作人，写入 sys_oper_log。
/// <list type="bullet">
/// <item>无特性 → 直接放行不记录；</item>
/// <item>异常（含业务异常）→ 记录 Status=0 与错误消息，异常继续向上抛由全局中间件统一响应；</item>
/// <item>日志写入自身失败仅告警，不影响主流程响应。</item>
/// </list>
/// </summary>
public sealed class OperLogActionFilter : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOperLogRepository _operLogRepository;
    private readonly UserContext _currentUser;
    private readonly ILogger<OperLogActionFilter> _logger;

    /// <summary>构造函数。</summary>
    /// <param name="operLogRepository">操作日志仓储。</param>
    /// <param name="currentUser">当前用户上下文。</param>
    /// <param name="logger">日志。</param>
    public OperLogActionFilter(IOperLogRepository operLogRepository, UserContext currentUser, ILogger<OperLogActionFilter> logger)
    {
        _operLogRepository = operLogRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<OperLogAttribute>();
        if (attribute is null)
        {
            await next();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var parameters = OperLogSanitizer.MaskSensitiveFields(JsonSerializer.Serialize(context.ActionArguments, JsonOptions));
        var executedContext = await next();
        stopwatch.Stop();

        try
        {
            var request = context.HttpContext.Request;
            var exception = executedContext.Exception;
            var actionName = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName
                             ?? context.ActionDescriptor.DisplayName
                             ?? string.Empty;
            await _operLogRepository.InsertAsync(new SysOperLog
            {
                Title = attribute.Module,
                BusinessType = attribute.BusinessType,
                Method = $"{context.Controller.GetType().FullName}.{actionName}",
                RequestMethod = request.Method,
                RequestUrl = TruncateUrl($"{request.Path}{request.QueryString}"),
                OperParam = OperLogSanitizer.Truncate(parameters),
                OperIp = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                OperUserName = _currentUser.UserName ?? string.Empty,
                Status = exception is null
                    ? Domain.Shared.Enums.ExecutionStatus.Success
                    : Domain.Shared.Enums.ExecutionStatus.Failure,
                ErrorMsg = exception is null ? null : OperLogSanitizer.Truncate(exception.Message),
                CostMs = stopwatch.ElapsedMilliseconds,
                OperTime = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            // 日志写入失败不影响主流程：仅记录告警
            _logger.LogWarning(ex, "写入操作日志失败：{Module}", attribute.Module);
        }
    }

    /// <summary>请求 URL 截断（列长度 255，含协议头时按字符上限裁剪）。</summary>
    private static string TruncateUrl(string url) => url.Length <= 255 ? url : url[..255];
}
