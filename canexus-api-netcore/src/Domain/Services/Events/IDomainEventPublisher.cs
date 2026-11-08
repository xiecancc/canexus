
using Domain.Events;

namespace Domain.Services.Events;
/// <summary>
/// 领域事件发布端口：核心业务流程关键节点经此发布领域事件，由实现方决定分发机制。
/// <para>当前唯一实现为 <c>Application/Events/OutboxDomainEventPublisher</c>（事务 Outbox）：
/// 事件随业务事务持久化到 <c>sys_outbox_message</c>，提交后同步投递、失败由后台 worker 重试，
/// 监听器最终由 MediatR 在进程内派发；**不是**单纯的进程内内存发布订阅。
/// 后续替换 / 扩展为消息队列时只需替换本端口的注册实现，业务代码零改动。</para>
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>发布领域事件（写入事务 Outbox，提交后投递给全部已订阅该事件类型的监听器）。</summary>
    /// <typeparam name="TEvent">领域事件类型。</typeparam>
    /// <param name="domainEvent">领域事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEvent;
}
