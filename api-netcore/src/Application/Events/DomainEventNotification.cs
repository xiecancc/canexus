namespace Application.Events;

using Domain.Events;
using MediatR;

/// <summary>
/// 领域事件通知信封：将领域事件包装为 MediatR 进程内通知（INotification），供监听器按事件类型订阅。
/// <para>泛型信封保证监听器拿到强类型事件；领域层事件模型不依赖 MediatR，替换分发框架仅需调整本层。</para>
/// </summary>
/// <typeparam name="TEvent">领域事件类型。</typeparam>
/// <param name="DomainEvent">被发布的领域事件。</param>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : DomainEvent;
