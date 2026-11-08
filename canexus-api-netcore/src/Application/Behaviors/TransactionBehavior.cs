
using Application.Contracts.Abstractions;
using Application.Events;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;
/// <summary>
/// 事务管道行为：对实现 <see cref="ICommand"/> 的写命令，在数据库事务中执行后续管道与处理器
/// （成功提交 / 异常自动回滚并原样透出），查询请求直接透放、不开启事务。
/// <para>事务边界覆盖「校验通过后的全部写入 + 领域事件发布（Outbox 持久化）」：处理器内不再手工管理
/// <see cref="IUnitOfWork"/>（历史上各处理器手工 <see cref="IUnitOfWork.RunInTransactionAsync{TResult}"/>，
/// 现统一收口到本行为，避免双开事务与遗漏事务两种问题）。注册顺序上位于 ValidationBehavior 之后
/// （最内层）：参数校验失败不开启事务；LoggingBehavior / PerformanceBehavior 的耗时统计包含事务开销。</para>
/// <para>提交后投递（P1-A2）：事务提交成功后排空本请求作用域收集的 Outbox 消息并同步投递——
/// 事件持久化与业务同事务（强一致），投递发生在提交之后（监听器不会读到中间态）；
/// 同步投递失败仅记录日志，消息保持待处理由后台 worker 重试，不影响业务响应。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="unitOfWork">工作单元（与仓储共用同一 SqlSugar 客户端，事务对全部仓储操作生效）。</param>
/// <param name="pendingDelivery">请求作用域 Outbox 待投递队列（发布器在事务内入队）。</param>
/// <param name="outboxDeliveryService">Outbox 投递服务（提交后同步投递；失败留待后台重试）。</param>
/// <param name="logger">日志器。</param>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    PendingOutboxDelivery pendingDelivery,
    IOutboxDeliveryService outboxDeliveryService,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 非写命令（查询 / 其他请求）直接透放：只读路径零事务开销
        if (request is not ICommand)
        {
            return await next();
        }

        // MediatR 12.4.1 的 RequestHandlerDelegate 不带令牌参数：令牌由 Send 在构建管道时捕获，
        // 这里再在事务委托入口显式检查一次，取消时由 UseTranAsync 捕获异常回滚而非提交
        var response = await unitOfWork.RunInTransactionAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return next();
            },
            cancellationToken);

        // 提交后投递：本请求事务内持久化的领域事件在此同步分发（失败不影响响应，转后台重试）
        await DeliverPendingOutboxAsync(cancellationToken);

        return response;
    }

    /// <summary>排空并投递本请求收集的 Outbox 消息；失败记录日志，消息保持待处理由后台 worker 重试。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DeliverPendingOutboxAsync(CancellationToken cancellationToken)
    {
        var messages = pendingDelivery.Drain();
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            // 提交后同步投递：传 null 表示消息尚未认领，由投递服务逐条条件认领（多副本并发下未抢到即跳过）
            await outboxDeliveryService.DeliverAsync(messages, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 提交后同步投递失败不阻断业务响应：消息保持待处理状态，由后台 worker 按 Outbox 配置重试
            logger.LogError(exception, "Outbox 提交后同步投递失败，{MessageCount} 条消息转后台重试", messages.Count);
        }
    }
}
