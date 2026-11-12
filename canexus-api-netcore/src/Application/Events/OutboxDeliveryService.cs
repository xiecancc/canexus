
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Shared.Options;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Events;

/// <summary>
/// Outbox 投递服务：认领消息 → 反序列化（仅接受 Domain.Events 下的 DomainEvent 子类）→ 经 MediatR 发布
/// （IsolatedNotificationPublisher 执行全部监听器后聚合失败并抛出）→ 仅发布成功才标记已处理；
/// 发布 / 反序列化失败记录次数 / 原因并按指数退避等待重试（超过上限进入失败 / 死信）。
/// <para>由事务提交后的 TransactionBehavior 与后台 worker 共用：提交后同步投递为主路径（自行逐条认领），
/// worker 兜底进程崩溃 / 同步投递失败后的重试（批量认领后传入 owner）。</para>
/// <para>幂等（P1-B）：消息 Id 为幂等键，未认领成功的消息直接跳过，多副本同一时刻仅一个副本投递。</para>
/// </summary>
public interface IOutboxDeliveryService
{
    /// <summary>逐条投递一批 Outbox 消息（单条失败不影响其他消息）。</summary>
    /// <param name="messages">待投递消息。</param>
    /// <param name="claimOwner">
    /// 已认领者标识：后台 worker 已批量认领时传入（跳过二次认领）；null 表示消息尚未认领（提交后同步投递路径，
    /// 由本服务逐条条件认领，未抢到即跳过）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task DeliverAsync(IReadOnlyList<OutboxMessage> messages, string? claimOwner, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOutboxDeliveryService" />
/// <remarks>构造函数。</remarks>
/// <param name="repository">Outbox 消息仓储（认领 / 状态更新经独立连接提交）。</param>
/// <param name="publisher">MediatR 通知发布器（已配置为 IsolatedNotificationPublisher）。</param>
/// <param name="options">Outbox 配置（重试上限、租约与退避参数）。</param>
/// <param name="logger">日志器。</param>
public sealed class OutboxDeliveryService(
    IOutboxMessageRepository repository,
    IPublisher publisher,
    IOptions<OutboxOption> options,
    ILogger<OutboxDeliveryService> logger) : IOutboxDeliveryService
{
    /// <inheritdoc />
    public async Task DeliverAsync(IReadOnlyList<OutboxMessage> messages, string? claimOwner, CancellationToken cancellationToken = default)
    {
        // 同步投递路径：每次投递生成独立 owner，逐条条件认领；worker 路径沿用其批量认领的 owner
        var owner = claimOwner ?? $"sync:{Environment.MachineName}:{Guid.NewGuid():N}";
        var lease = TimeSpan.FromSeconds(options.Value.LeaseSeconds);

        foreach (var message in messages)
        {
            if (claimOwner is null && !await repository.TryClaimAsync(message.Id, owner, lease, cancellationToken))
            {
                // 已被其他副本认领 / 已处理：幂等跳过，不重复投递
                logger.LogDebug("Outbox 消息 {MessageId} 已被其他副本认领或已处理，跳过投递", message.Id);
                continue;
            }

            try
            {
                var domainEvent = OutboxEventSerializer.Deserialize(message.EventType, message.Payload);
                // 把消息 Id 随信封传给监听器：带副作用的监听器（如站外通知渠道）据此做幂等去重，
                // 避免「其他监听器失败 → 本条消息重投 → 已成功的渠道被重复投递」（P1-5 / D4）。
                await publisher.Publish(new DomainEventNotification(domainEvent, message.Id), cancellationToken);
                if (!await repository.MarkProcessedAsync(message.Id, owner, DateTime.UtcNow, cancellationToken))
                {
                    // 认领已被其他副本接管（租约过期）：本轮结果作废，不记失败也不重复投递
                    logger.LogWarning("Outbox 消息 {MessageId} 的认领已易主，本轮投递结果不再落库", message.Id);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var attempt = message.AttemptCount + 1;
                var nextRetryAt = CalculateNextRetryAt(attempt);
                logger.LogError(exception,
                    "Outbox 投递失败（第 {Attempt}/{MaxAttempts} 次尝试，消息 {MessageId}，事件 {EventType}）",
                    attempt, options.Value.MaxAttempts, message.Id, message.EventType);
                await repository.RecordFailureAsync(
                    message.Id,
                    owner,
                    Truncate(DescribeFailure(exception)),
                    attempt,
                    options.Value.MaxAttempts,
                    nextRetryAt,
                    cancellationToken);
            }
        }
    }

    /// <summary>把聚合异常展开为可入库的失败原因，避免只留下「One or more errors occurred」。</summary>
    /// <param name="exception">投递阶段捕获的异常。</param>
    /// <returns>用于 LastError 的摘要文本。</returns>
    private static string DescribeFailure(Exception exception)
    {
        if (exception is not AggregateException aggregate)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }

        var parts = new List<string>();
        foreach (var inner in aggregate.Flatten().InnerExceptions)
        {
            parts.Add($"{inner.GetType().Name}: {inner.Message}");
        }

        return string.Join(" | ", parts);
    }

    /// <summary>按 sys_outbox_message.LastError 列长度截断失败原因，避免 DB 写入再次失败。</summary>
    /// <param name="error">失败原因。</param>
    /// <returns>不超过列长度的摘要文本。</returns>
    private static string Truncate(string error) => error.Length <= 1000 ? error : error[..1000];

    /// <summary>计算指数退避的下一次重试时间：base * 2^(attempt-1)，上限为配置的最大延迟。</summary>
    /// <param name="attempt">本次失败后的尝试次数（从 1 开始）。</param>
    /// <returns>下一次允许重试的时间（UTC）。</returns>
    private DateTime CalculateNextRetryAt(int attempt)
    {
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 10);
        var delaySeconds = Math.Min(
            options.Value.BaseRetryDelaySeconds * Math.Pow(2, exponent),
            options.Value.MaxRetryDelaySeconds);
        return DateTime.UtcNow.AddSeconds(delaySeconds);
    }
}
