namespace Application.Events;

using Domain.Events;
using Domain.Services.Events;
using MediatR;

/// <summary>基于 MediatR 的领域事件发布器：业务关键节点经此发布事件，全部已订阅监听器依次收到通知。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="publisher">MediatR 通知发布器。</param>
public sealed class MediatRDomainEventPublisher(IPublisher publisher) : IDomainEventPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEvent
    {
        await publisher.Publish(new DomainEventNotification(domainEvent), cancellationToken);
    }
}
