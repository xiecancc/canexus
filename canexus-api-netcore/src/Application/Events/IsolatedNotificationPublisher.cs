using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Events;
/// <summary>
/// 监听器隔离的通知发布策略：逐一同步调用各监听器，单个监听器异常仅记录 Error 日志并降级，
/// 不阻断其余监听器执行，也不向上传播使业务请求失败（如缓存失效失败不应使 API 500）。
/// <para>监听器承载的多为缓存清理、审计记录等旁路动作：失败的一致性由缓存 TTL 过期兜底最终一致，
/// 业务可用性优先。请求取消（<see cref="OperationCanceledException"/>）不做降级、原样向上传播。</para>
/// <para>扩展约定：新增监听器（INotificationHandler 实现）自动获得本隔离语义，无需自行 try-catch。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class IsolatedNotificationPublisher(ILogger<IsolatedNotificationPublisher> logger) : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var executor in handlerExecutors)
        {
            try
            {
                await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 监听器失败降级：记录 Error 后继续执行其余监听器，请求不因此失败
                logger.LogError(exception, "领域事件监听器执行失败已降级：监听器 {HandlerType}，通知 {NotificationType}",
                    executor.HandlerInstance.GetType().Name, notification.GetType().Name);
            }
        }
    }
}
