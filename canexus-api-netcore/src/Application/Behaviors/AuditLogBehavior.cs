
using System.Diagnostics;
using Application.Contracts.Abstractions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Json;
using Domain.Shared.Logging;
using Infrastructure.Shared.Contexts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;
/// <summary>
/// 默认审计管道行为（P0）：对所有写命令（<see cref="ICommand"/>）默认写入操作日志（sys_oper_log），
/// 覆盖「端点漏标 [OperLog] 即无审计」的缺口——默认记录、显式接管，而非隐式遗漏。
/// <list type="bullet">
/// <item>端点标注 [OperLog] → OperLogActionFilter 已在管道执行前标记接管
/// （<see cref="IOperationContext.MarkOperLogHandled"/>），本行为跳过，避免同一操作双份记录；</item>
/// <item>记录位置在本行为（管道最外层）：成功路径在事务提交后写入、失败路径在事务回滚后写入，
/// 审计记录不随业务事务回滚；</item>
/// <item>载荷序列化 + 敏感字段脱敏（<see cref="SensitiveDataMasker"/>）+ 截断（2000 字符）；
/// 序列化失败（如 Stream 载荷）时放弃载荷仅保留命令元数据；</item>
/// <item>审计写入失败经有界重试（最多 2 次、短间隔）后仍失败仅记录最终失败，不影响业务响应；<c>Outbox</c> 补偿留待后续。</item>
/// </list>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储。</param>
/// <param name="currentUser">当前用户上下文（操作人姓名 / Id）。</param>
/// <param name="operationContext">请求上下文（链路 TraceId 与接管标记）。</param>
/// <param name="logger">日志器。</param>
public sealed class AuditLogBehavior<TRequest, TResponse>(
    IOperLogRepository operLogRepository,
    IUserContext currentUser,
    IOperationContext operationContext,
    ILogger<AuditLogBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>默认审计记录的请求方式标识（区别于 HTTP 方法的 MediatR 命令通道）。</summary>
    private const string CommandRequestMethod = "COMMAND";

    /// <summary>载荷 / 错误消息截断长度（与 sys_oper_log 列长度一致）。</summary>
    private const int MaxTextLength = 2000;

    /// <summary>请求 URL 截断长度（与 sys_oper_log.RequestUrl 列长度一致）。</summary>
    private const int MaxUrlLength = 255;

    /// <summary>审计写入最大尝试次数（首次 + 1 次有界重试）。</summary>
    private const int MaxAuditWriteAttempts = 2;

    /// <summary>审计写入重试前的短间隔（毫秒）。</summary>
    private const int AuditRetryDelayMs = 50;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // 已取消的请求在管道入口即中止；业务成功 / 失败路径的审计写入仍用 CancellationToken.None 保证落库
        cancellationToken.ThrowIfCancellationRequested();
        // 仅审计写命令；查询请求直接透放
        if (request is not ICommand)
        {
            return await next();
        }

        // 端点标注 [OperLog] 时过滤器已接管（含 HTTP 请求细节），管道默认审计跳过避免重复
        if (operationContext.IsOperLogHandled)
        {
            return await next();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();

            // 用 CancellationToken.None 写入：取消路径不丢审计记录
            await WriteAuditAsync(request, success: true, exception: null, stopwatch.ElapsedMilliseconds, CancellationToken.None);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            // 失败审计须在事务回滚后仍能落库：事务由内层 TransactionBehavior 管理并已结束，
            // 此处插入为独立自动提交（CancellationToken.None 保证取消路径同样留下审计痕迹）
            await WriteAuditAsync(request, success: false, exception, stopwatch.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }
    }

    /// <summary>写入默认审计记录（有界重试；最终失败仅记录日志，不阻断业务响应）。</summary>
    /// <param name="request">当前命令请求。</param>
    /// <param name="success">业务是否成功。</param>
    /// <param name="exception">业务异常（成功时为 null）。</param>
    /// <param name="costMs">业务耗时（毫秒）。</param>
    /// <param name="cancellationToken">写入令牌（调用方统一传 <c>CancellationToken.None</c>，避免取消路径丢审计）。</param>
    /// <returns>表示异步写入的 <c>Task</c>。</returns>
    private async Task WriteAuditAsync(TRequest request, bool success, Exception? exception, long costMs, CancellationToken cancellationToken)
    {
        var commandName = typeof(TRequest).Name;
        try
        {
            for (var attempt = 1; attempt <= MaxAuditWriteAttempts; attempt++)
            {
                try
                {
                    // 每次尝试重建实体：避免首次已落库但抛异常时，复用带主键的实体造成重复键冲突
                    await operLogRepository.InsertAsync(BuildAuditLog(request, success, exception, costMs), cancellationToken);
                    return;
                }
                catch (Exception writeException) when (attempt < MaxAuditWriteAttempts)
                {
                    logger.LogWarning(writeException, "默认审计写入失败，短间隔后重试（{Attempt}/{MaxAttempts}）：{Command}",
                        attempt, MaxAuditWriteAttempts, commandName);
                    await Task.Delay(AuditRetryDelayMs, CancellationToken.None);
                }
                catch (Exception writeException)
                {
                    // 有界重试耗尽：记录最终失败；审计写入 Outbox 补偿留待后续，本批不新建基础设施
                    logger.LogError(writeException, "默认审计写入最终失败（已尝试 {Attempts} 次）：{Command}",
                        MaxAuditWriteAttempts, commandName);
                    return;
                }
            }
        }
        catch (Exception compensationException)
        {
            // 补偿流程自身异常（如重试等待）同样不得影响业务响应
            logger.LogError(compensationException, "默认审计补偿流程异常：{Command}", commandName);
        }
    }

    /// <summary>构造默认审计记录（元数据 / 载荷脱敏 / 截断）。</summary>
    /// <param name="request">当前命令请求。</param>
    /// <param name="success">业务是否成功。</param>
    /// <param name="exception">业务异常（成功时为 null）。</param>
    /// <param name="costMs">业务耗时（毫秒）。</param>
    /// <returns>待写入的操作日志实体。</returns>
    private SysOperLog BuildAuditLog(TRequest request, bool success, Exception? exception, long costMs)
    {
        var commandType = request.GetType();
        return new SysOperLog
        {
            Title = commandType.Name,
            BusinessType = OperBusinessType.None,
            Method = commandType.FullName ?? commandType.Name,
            // F2：经 IOperationContext 补真实 HTTP 元数据（RequestUrl = 请求路径、OperIp = 客户端 IP）；
            // 非 HTTP 上下文（后台任务 / Quartz）三项均为 null，此时保持原占位回退值（命令类名 / 空 IP），不抛异常。
            // RequestPath 由 HttpContextOperationContext 保证仅含路径、不含 QueryString。
            // RequestMethod 保持固定的「COMMAND」通道标识：它标识该记录来自命令管道（区别于控制器通道的
            // HTTP 动词），不能被真实 HTTP 方法覆盖——oper-log 查询与测试均按 requestMethod == COMMAND 过滤。
            RequestMethod = CommandRequestMethod,
            RequestUrl = TruncateUrl(operationContext.RequestPath) ?? commandType.Name,
            OperIp = operationContext.ClientIp,
            OperParam = TrySerializeParam(request),
            OperUserName = currentUser.UserName ?? string.Empty,
            UserId = currentUser.UserId,
            TraceId = operationContext.TraceId,
            Status = success ? ExecutionStatus.Success : ExecutionStatus.Failure,
            ErrorMsg = exception is null
                ? null
                : Truncate(SensitiveDataMasker.MaskFreeText(exception.Message)),
            CostMs = costMs,
            OperTime = DateTime.UtcNow
        };
    }

    /// <summary>序列化命令载荷并脱敏截断；不可序列化载荷（Stream 等）返回 null（仅保留命令元数据）。</summary>
    private static string? TrySerializeParam(TRequest request)
    {
        try
        {
            return Truncate(SensitiveDataMasker.MaskSensitiveFields(JsonHelper.Serialize(request)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>文本截断（列长度上限，超长裁剪）。</summary>
    private static string? Truncate(string? text) => text is null || text.Length <= MaxTextLength ? text : text[..MaxTextLength];

    /// <summary>请求 URL 截断（列长度 255，超长裁剪）。</summary>
    /// <param name="url">请求路径。</param>
    /// <returns>截断后的路径；null 原样返回。</returns>
    private static string? TruncateUrl(string? url) => url is null || url.Length <= MaxUrlLength ? url : url[..MaxUrlLength];
}
