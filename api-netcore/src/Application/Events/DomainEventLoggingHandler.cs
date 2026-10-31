namespace Application.Events;

using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// 领域事件审计日志监听器（通用监听器）：订阅全部领域事件并写入结构化日志，
/// 为事件链路提供统一审计跟踪，也是各领域事件最基础的响应动作。
/// <para>领域专属监听器（权限缓存 / 字典缓存 / 配置缓存失效等）只处理各自领域事件，
/// 本监听器负责兜底记录，二者职责互补。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class DomainEventLoggingHandler(ILogger<DomainEventLoggingHandler> logger)
    : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        logger.LogInformation("领域事件已发布：{EventName}（{EventType}），聚合根 {AggregateId}，载荷 {@Payload}",
            domainEvent.EventName, domainEvent.EventType, domainEvent.AggregateId, domainEvent.Payload);
        return Task.CompletedTask;
    }
}
