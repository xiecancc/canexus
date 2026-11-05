
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
/// <item>审计写入自身失败仅告警，不影响业务响应。</item>
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

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
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

    /// <summary>写入默认审计记录（元数据齐全；写入失败仅告警，不阻断业务响应）。</summary>
    private async Task WriteAuditAsync(TRequest request, bool success, Exception? exception, long costMs, CancellationToken cancellationToken)
    {
        try
        {
            var commandType = request.GetType();
            await operLogRepository.InsertAsync(new SysOperLog
            {
                Title = commandType.Name,
                BusinessType = OperBusinessType.None,
                Method = commandType.FullName ?? commandType.Name,
                RequestMethod = CommandRequestMethod,
                RequestUrl = commandType.Name,
                OperParam = TrySerializeParam(request),
                OperUserName = currentUser.UserName ?? string.Empty,
                UserId = currentUser.UserId,
                TraceId = operationContext.TraceId,
                Status = success ? ExecutionStatus.Success : ExecutionStatus.Failure,
                ErrorMsg = exception is null ? null : Truncate(exception.Message),
                CostMs = costMs,
                OperTime = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception writeException)
        {
            logger.LogWarning(writeException, "默认审计写入失败：{Command}", typeof(TRequest).Name);
        }
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
}
