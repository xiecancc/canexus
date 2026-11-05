using System.Diagnostics;
using Infrastructure.Shared.Contexts;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Logging;

/// <summary>
/// 基于 IHttpContextAccessor 的请求上下文实现（单例注册，经 Accessor 取当前请求）。
/// <para>审计标记存于 HttpContext.Items（请求作用域）；非 HTTP 上下文（后台任务）标记恒为 false、
/// TraceId 回退 <see cref="Activity.Current"/>，保证后台写命令同样进入默认审计。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="accessor">HTTP 上下文访问器。</param>
public sealed class HttpContextOperationContext(IHttpContextAccessor accessor) : IOperationContext
{
    /// <summary>操作日志已由 ActionFilter 接管的请求作用域标记键。</summary>
    private const string OperLogHandledKey = "SuperAdmin.OperLogHandled";

    /// <inheritdoc />
    public string? TraceId
    {
        get
        {
            // HTTP 请求：取 TraceIdMiddleware 写入的 TraceIdentifier（与响应头 X-Trace-Id 同值）
            var traceId = accessor.HttpContext?.TraceIdentifier;
            if (!string.IsNullOrEmpty(traceId))
            {
                return traceId;
            }

            // 非 HTTP 上下文（后台任务 / Quartz）：回退当前 Activity 的 TraceId
            return Activity.Current?.TraceId.ToString();
        }
    }

    /// <inheritdoc />
    public bool IsOperLogHandled =>
        accessor.HttpContext is not null
        && accessor.HttpContext.Items.TryGetValue(OperLogHandledKey, out var handled)
        && handled is true;

    /// <inheritdoc />
    public void MarkOperLogHandled()
    {
        accessor.HttpContext?.Items.TryAdd(OperLogHandledKey, true);
    }
}
