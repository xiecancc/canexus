using API.Logging;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Authorization;
/// <summary>
/// 授权拒绝审计（P1-1）：全局权限过滤器 / 租户 / 强制改密等拒绝分支在 MVC 授权过滤器短路后
/// 不再经过 <c>OperLogActionFilter</c>，越权尝试会漏记操作日志。本辅助类提供统一的「尽力而为」拒绝审计：
/// 从 <c>HttpContext.RequestServices</c> 解析 <c>IOperLogRepository</c>，落一条业务类型为「其他」、
/// 状态为失败、含脱敏简要原因与真实请求方法 / URL / 客户端 IP 的记录。
/// <para>写入失败（含服务未注册、请求已取消、日志库不可用）仅告警，绝不影响已短路的拒绝响应；
/// 审计写入统一使用 <c>CancellationToken.None</c>，不复用可能已取消的请求令牌。</para>
/// </summary>
public static class AuthorizationRejectionAudit
{
    /// <summary>拒绝审计的模块标题。</summary>
    private const string RejectionTitle = "访问拒绝";

    /// <summary>请求 URL 列长度上限（与 sys_oper_log.RequestUrl 一致）。</summary>
    private const int MaxUrlLength = 255;

    /// <summary>写入一条拒绝审计记录（尽力而为，异常内部吞掉，仅告警）。</summary>
    /// <param name="context">当前 HTTP 上下文（用于取请求方法 / URL / 客户端 IP 与请求作用域服务）。</param>
    /// <param name="reason">简要拒绝原因（写入前经自由文本脱敏与截断，不记录请求体）。</param>
    /// <param name="errorCode">业务错误码（如 <c>ErrorCodes.AuthForbidden</c>），随原因一并记录便于检索。</param>
    /// <param name="ct">保留参数以兼容调用约定；审计写入统一用 <c>CancellationToken.None</c>，不采用可能已取消的请求令牌。</param>
    /// <returns>表示异步写入的 <c>Task</c>；任何写入异常均已在内部处理，调用方可安全 fire-and-forget。</returns>
    public static async Task WriteAsync(HttpContext context, string reason, int errorCode, CancellationToken ct = default)
    {
        // 有意不使用 ct：拒绝分支的请求令牌可能已取消，统一以 CancellationToken.None 写入，避免审计丢失。
        ILogger? logger = null;
        try
        {
            logger = context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("API.Authorization.AuthorizationRejectionAudit");
            var repository = context.RequestServices.GetService<IOperLogRepository>();
            if (repository is null)
            {
                return;
            }

            // 操作人上下文为可选服务：缺失（如测试宿主 / 后台上下文）时留空，不影响审计落库
            var currentUser = context.RequestServices.GetService<IUserContext>();
            var request = context.Request;
            var requestUrl = Truncate(SensitiveDataMasker.MaskQueryString(
                $"{request.Path}{request.QueryString}"), MaxUrlLength);
            var maskedReason = SensitiveDataMasker.MaskFreeText(reason);

            await repository.InsertAsync(new SysOperLog
            {
                Title = RejectionTitle,
                // 拒绝类操作无对应业务枚举，统一归为「其他」（与 AuditLogBehavior 默认兜底一致）
                BusinessType = OperBusinessType.None,
                Method = $"{nameof(AuthorizationRejectionAudit)}.{nameof(WriteAsync)}",
                RequestMethod = request.Method,
                RequestUrl = requestUrl,
                OperIp = context.Connection.RemoteIpAddress?.ToString(),
                OperUserName = currentUser?.UserName ?? string.Empty,
                UserId = currentUser?.UserId,
                TraceId = context.TraceIdentifier,
                Status = ExecutionStatus.Failure,
                ErrorMsg = OperLogSanitizer.Truncate($"[{errorCode}] {maskedReason}"),
                CostMs = 0,
                OperTime = DateTime.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // 审计写入失败不得影响拒绝响应：仅告警（logger 可能同样解析失败，回退空日志器）
            (logger ?? NullLogger.Instance).LogWarning(exception, "拒绝审计写入失败：{Reason}", reason);
        }
    }

    /// <summary>按列长度上限截断文本（空值返回空字符串）。</summary>
    /// <param name="value">原始文本。</param>
    /// <param name="maxLength">长度上限。</param>
    /// <returns>截断后的文本。</returns>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
