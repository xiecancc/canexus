namespace Domain.Services.Events;

using Domain.Events;

/// <summary>
/// 领域事件发布端口：核心业务流程关键节点经此发布领域事件，由实现方决定分发机制
/// （当前为进程内发布订阅，后续可替换 / 扩展为消息队列等，业务代码零改动）。
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>发布领域事件，通知全部已订阅该事件类型的监听器。</summary>
    /// <typeparam name="TEvent">领域事件类型。</typeparam>
    /// <param name="domainEvent">领域事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEvent;
}
