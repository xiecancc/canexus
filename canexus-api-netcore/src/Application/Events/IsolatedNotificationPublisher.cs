using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Events;
/// <summary>
/// 监听器隔离的通知发布策略：逐一同步调用全部监听器，单个监听器失败仅记录 Error 日志且不阻断其余监听器执行；
/// 全部监听器执行完毕后，把收集到的失败以 <see cref="AggregateException"/> 向上传播，
/// 由 Outbox 投递服务记录失败并进入重试，避免「监听器失败却把消息标记为已处理」。
/// <para>业务可用性不受影响：提交后同步投递的异常由 <c>TransactionBehavior</c> 兜底（不阻断业务响应），
/// 消息以 Pending 状态留给后台 worker 重投；因此监听器必须可重入（缓存失效 / 版本失效等按幂等实现）。
/// 请求取消（<see cref="OperationCanceledException"/>）不做降级、立即原样向上传播。</para>
/// <para>扩展约定：新增监听器（INotificationHandler 实现）自动获得本隔离语义，无需自行 try-catch。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="logger">日志器。</param>
public sealed class IsolatedNotificationPublisher(ILogger<IsolatedNotificationPublisher> logger) : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var executor in handlerExecutors)
        {
            try
            {
                await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消属正常控制流：不降级、不汇总，立即原样向上传播
                throw;
            }
            catch (Exception exception)
            {
                // 单个监听器失败不阻断其余监听器，但必须让 Outbox 感知以进入重试
                logger.LogError(exception, "领域事件监听器执行失败：监听器 {HandlerType}，通知 {NotificationType}",
                    executor.HandlerInstance.GetType().Name, notification.GetType().Name);
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("一个或多个领域事件监听器执行失败（由 Outbox 记录失败并重试）", failures);
        }
    }
}
