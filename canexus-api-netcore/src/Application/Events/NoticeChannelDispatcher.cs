using Domain.Events;
using Domain.Services.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Events;
/// <summary>
/// 通知公告渠道分发监听器：公告创建成功后，将标题与正文投递到全部已注册渠道（邮件 / 短信等站外载体）。
/// <para>渠道间完全隔离：单渠道投递失败（实现方自吞异常）不影响其他渠道，更不阻塞公告创建主流程；
/// 渠道未注册（如邮件渠道未启用）时分发自然为空操作。</para>
/// </summary>
/// <param name="channels">全部已注册通知渠道（DI 注入集合，未注册时为空集合）。</param>
/// <param name="logger">日志。</param>
public sealed class NoticeChannelDispatcher(
    IEnumerable<INotificationChannel> channels,
    ILogger<NoticeChannelDispatcher> logger) : DomainEventHandler<NoticeCreatedEvent>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(NoticeCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        var notice = domainEvent.NewEntity;
        var registered = channels as INotificationChannel[] ?? [.. channels];
        if (registered.Length == 0)
        {
            return;
        }

        foreach (var channel in registered)
        {
            try
            {
                await channel.SendAsync(notice.Title, notice.Content, cancellationToken);
                logger.LogInformation("通知公告已投递到渠道 {Channel}：{Title}", channel.Name, notice.Title);
            }
            catch (OperationCanceledException)
            {
                // 取消语义向上传播（宿主关停 / 请求取消）
                throw;
            }
            catch (Exception ex)
            {
                // 实现方应自吞投递异常，此处兜底防御：单渠道故障不拖垮公告创建
                logger.LogWarning(ex, "通知公告投递到渠道 {Channel} 时发生未处理异常：{Title}", channel.Name, notice.Title);
            }
        }
    }
}
