using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;

namespace Application.Events;

/// <summary>
/// 基于事务 Outbox 的领域事件发布器（P1-A2）：事件随业务事务持久化到 Outbox 表，
/// 并加入请求作用域待投递队列；事务提交成功后由 TransactionBehavior 统一投递。
/// <para>相比原进程内即时发布：事件与业务状态强一致（命令失败事件一并回滚）；
/// 进程崩溃 / 投递失败由后台 worker 从 Outbox 表恢复重试，杜绝事件丢失。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="repository">Outbox 消息仓储（写入随业务事务提交）。</param>
/// <param name="pendingDelivery">请求作用域待投递队列。</param>
public sealed class OutboxDomainEventPublisher(
    IOutboxMessageRepository repository,
    PendingOutboxDelivery pendingDelivery) : IDomainEventPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEvent
    {
        var message = OutboxMessage.Create(domainEvent.GetType().FullName!, OutboxEventSerializer.Serialize(domainEvent));
        await repository.InsertAsync(message, cancellationToken);
        pendingDelivery.Add(message);
    }
}
