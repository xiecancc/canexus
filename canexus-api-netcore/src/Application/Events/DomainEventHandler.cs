
using Domain.Events;
using MediatR;

namespace Application.Events;
/// <summary>
/// 领域事件监听器基类：订阅非泛型通知信封，仅响应 <typeparamref name="TEvent"/> 类型的事件，
/// 屏蔽各监听器重复的「类型过滤守卫」样板，子类只实现关心事件的处理逻辑。
/// <para>泛型实参传 <see cref="DomainEvent"/> 即订阅全部事件（is 模式恒匹配）。</para>
/// <para>局限：C# 类单继承，一个监听器类无法同时继承多个泛型实例——
/// 响应多种事件类型的监听器（如权限缓存失效）应直接实现 INotificationHandler 并在处理函数内模式匹配。</para>
/// </summary>
/// <typeparam name="TEvent">监听器响应的领域事件类型。</typeparam>
public abstract class DomainEventHandler<TEvent> : INotificationHandler<DomainEventNotification>
    where TEvent : DomainEvent
{
    /// <inheritdoc />
    public Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        return notification.DomainEvent is TEvent domainEvent
            ? HandleAsync(domainEvent, cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>子类实现的事件处理逻辑（仅收到匹配类型的事件）。</summary>
    /// <param name="domainEvent">领域事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
