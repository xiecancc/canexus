namespace Domain.Services.Notifications;

/// <summary>
/// 通知投递渠道端口：将通知公告投递到站外载体（邮件 / 短信 / 即时消息等）。
/// <para>站内展示（通知公告页 / 首页卡片）由 sys_notice 落库天然承载，本端口只负责外发渠道；
/// 实现方职责：自身可用性自检（如未启用时静默跳过）、投递失败自吞并记录日志（外发失败不得阻塞公告创建主流程）。</para>
/// </summary>
public interface INotificationChannel
{
    /// <summary>渠道名称（日志与扩展标识，如 "email" / "sms"）。</summary>
    public string Name { get; }

    /// <summary>投递一条通知到该渠道。</summary>
    /// <param name="title">通知标题。</param>
    /// <param name="content">通知正文（富文本，渠道自行决定渲染方式）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步投递的任务。</returns>
    public Task SendAsync(string title, string content, CancellationToken cancellationToken = default);
}
