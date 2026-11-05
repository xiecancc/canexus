namespace Infrastructure.Shared.Contexts;

/// <summary>
/// 请求上下文抽象：为应用层管道（AuditLogBehavior 默认审计）提供 HTTP 请求侧的链路与审计协同信息。
/// <para>实现基于 IHttpContextAccessor（Infrastructure 层），非 HTTP 上下文（后台任务 / Quartz 作业）
/// 下 TraceId 回退 Activity、审计标记恒为 false（后台写命令也走默认审计）。</para>
/// </summary>
public interface IOperationContext
{
    /// <summary>当前请求链路 TraceId（与响应头 X-Trace-Id 同值）；无请求上下文时回退当前 Activity 的 TraceId，再无则为 null。</summary>
    public string? TraceId { get; }

    /// <summary>本次请求的操作日志是否已由 OperLogActionFilter 负责（端点标注 [OperLog] 时，管道默认审计跳过避免重复）。</summary>
    public bool IsOperLogHandled { get; }

    /// <summary>标记本次请求的操作日志由 OperLogActionFilter 负责（须在 MediatR 管道执行前调用）。</summary>
    public void MarkOperLogHandled();
}
