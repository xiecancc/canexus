using Domain.Entities;
using Domain.Events;
using Domain.Services.Notifications;
using Infrastructure.Shared.Caching;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Events;
/// <summary>
/// 通知公告渠道分发监听器：公告创建成功后，将标题与正文投递到全部已注册渠道（邮件 / 短信等站外载体）。
/// <para>渠道间完全隔离：单渠道投递失败（实现方自吞异常）不影响其他渠道，更不阻塞公告创建主流程；
/// 渠道未注册（如邮件渠道未启用）时分发自然为空操作。</para>
/// <para>幂等（P1-5 / D4）：Outbox 是至少一次投递，<see cref="IsolatedNotificationPublisher"/> 在任一监听器失败时
/// 会抛 <see cref="AggregateException"/> 令整条消息重投，本监听器即使自身成功也会被连带重放。
/// 因此按「Outbox 消息 Id + 渠道名」写入已投递标记，重投时跳过已成功的渠道，避免重复打扰用户。
/// 标记仅在投递成功后写入；渠道失败不写标记，重投时仍会补发该渠道。
/// 非 Outbox 路径（<c>OutboxMessageId</c> 为 null，如进程内直发）保持原有逐渠道分发语义、不做去重。</para>
/// <para>并发语义：Outbox 认领租约（TryClaimAsync）已保证同一消息在同一时刻仅一个副本投递；
/// 缓存标记覆盖「失败后延时重投 / 死信重放」这一主要的顺序重放场景，不额外引入分布式锁。
/// 直接实现 <see cref="INotificationHandler{TNotification}"/> 而非继承 <see cref="DomainEventHandler{TEvent}"/>，
/// 是为了在处理函数内读取整个信封上的消息 Id（基类只把领域事件传给子类）。</para>
/// </summary>
/// <param name="channels">全部已注册通知渠道（DI 注入集合，未注册时为空集合）。</param>
/// <param name="cache">应用缓存（已投递标记：L1 短驻留加速、L2 长驻留跨副本去重）。</param>
/// <param name="logger">日志。</param>
public sealed class NoticeChannelDispatcher(
    IEnumerable<INotificationChannel> channels,
    ICache cache,
    ILogger<NoticeChannelDispatcher> logger) : INotificationHandler<DomainEventNotification>
{
    /// <summary>
    /// 已投递标记 TTL：L1 5 分钟仅供本实例加速，L2 30 天覆盖 Outbox 重试与死信保留窗口
    /// （OutboxOption 默认 FailedRetentionDays = 30）。公告数量有限，长期标记不会造成明显内存压力。
    /// </summary>
    private static readonly CacheTtlProfile MarkerTtl =
        new(TimeSpan.FromMinutes(5), TimeSpan.FromDays(30));

    /// <summary>已投递标记的固定值（只判断键是否存在，值内容无业务含义）。</summary>
    private const string DeliveredMarker = "1";

    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        if (notification.DomainEvent is not NoticeCreatedEvent created)
        {
            return;
        }

        var registered = channels as INotificationChannel[] ?? [.. channels];
        if (registered.Length == 0)
        {
            return;
        }

        var notice = created.NewEntity;
        foreach (var channel in registered)
        {
            var markerKey = notification.OutboxMessageId is Guid messageId
                ? $"outbox:notice-channel:{messageId:N}:{channel.Name}"
                : null;

            if (markerKey is not null
                && !string.IsNullOrEmpty(await cache.GetAsync<string>(markerKey, cancellationToken)))
            {
                logger.LogInformation(
                    "Outbox 消息 {MessageId} 已投递到渠道 {Channel}，跳过重复发送：{Title}",
                    notification.OutboxMessageId, channel.Name, notice.Title);
                continue;
            }

            if (!await SendSafelyAsync(channel, notice, cancellationToken))
            {
                continue;
            }

            if (markerKey is not null)
            {
                await cache.SetAsync(markerKey, DeliveredMarker, MarkerTtl, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 向单渠道投递并隔离其故障：返回是否投递成功，成功才允许写已投递标记。
    /// 取消语义向上传播（宿主关停 / 请求取消），其余异常仅记录告警。
    /// </summary>
    /// <param name="channel">目标渠道。</param>
    /// <param name="notice">公告实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>投递成功返回 true；渠道故障返回 false。</returns>
    private async Task<bool> SendSafelyAsync(
        INotificationChannel channel,
        SysNotice notice,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendAsync(notice.Title, notice.Content, cancellationToken);
            logger.LogInformation("通知公告已投递到渠道 {Channel}：{Title}", channel.Name, notice.Title);
            return true;
        }
        catch (OperationCanceledException)
        {
            // 取消属正常控制流：不降级、不写标记，立即原样向上传播
            throw;
        }
        catch (Exception ex)
        {
            // 实现方应自吞投递异常，此处兜底防御：单渠道故障不拖垮公告创建，也不阻断后续渠道
            logger.LogWarning(ex, "通知公告投递到渠道 {Channel} 时发生未处理异常：{Title}", channel.Name, notice.Title);
            return false;
        }
    }
}
