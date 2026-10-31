namespace Application.Events;

using Domain.Events;
using Microsoft.Extensions.Logging;

/// <summary>
/// 领域事件审计日志监听器（通用监听器）：订阅全部领域事件并写入结构化日志，
/// 为事件链路提供统一审计跟踪，也是各领域事件最基础的响应动作。
/// <para>泛型实参传 DomainEvent 表示全量订阅；领域专属监听器（权限 / 字典 / 配置缓存失效等）
/// 只处理各自领域事件，本监听器负责兜底记录，二者职责互补。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class DomainEventLoggingHandler(ILogger<DomainEventLoggingHandler> logger)
    : DomainEventHandler<DomainEvent>
{
    /// <inheritdoc />
    protected override Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("领域事件已发布：{EventName}，发生时间 {OccurredAt}",
            domainEvent.EventName, domainEvent.OccurredAt);
        return Task.CompletedTask;
    }
}
