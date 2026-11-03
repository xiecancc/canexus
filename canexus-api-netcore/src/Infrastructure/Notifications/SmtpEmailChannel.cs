using System.Net;
using System.Net.Mail;
using Domain.Services.Notifications;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Notifications;

/// <summary>
/// SMTP 邮件通知渠道实现。
/// <para>Enabled=false 时静默跳过（不建立连接）；投递异常自吞并记录告警日志——
/// 外发失败不阻塞公告创建主流程（最终一致：站内展示不受影响）。</para>
/// </summary>
/// <param name="options">邮件配置选项（IOptionsMonitor 支持热加载开关与投递参数）。</param>
/// <param name="logger">日志。</param>
public class SmtpEmailChannel(IOptionsMonitor<MailOption> options, ILogger<SmtpEmailChannel> logger) : INotificationChannel
{
    /// <inheritdoc />
    public string Name => "email";

    /// <inheritdoc />
    public async Task SendAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var option = options.CurrentValue;
        if (!option.Enabled)
        {
            return;
        }

        // 简单 HTML 包装：通知正文为富文本，邮件侧统一作为 HTML 正文投递（纯文本场景由邮件客户端降级渲染）
        var body = $"<h3>{WebUtility.HtmlEncode(title)}</h3>{content}";

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(option.From),
                Subject = title,
                Body = body,
                IsBodyHtml = true
            };
            foreach (var recipient in option.Recipients)
            {
                message.To.Add(recipient);
            }

            using var client = new SmtpClient(option.Host, option.Port)
            {
                EnableSsl = option.UseSsl,
                Timeout = option.TimeoutMilliseconds
            };
            if (!string.IsNullOrWhiteSpace(option.Username))
            {
                client.Credentials = new NetworkCredential(option.Username, option.Password);
            }

            await client.SendMailAsync(message, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "邮件通知投递失败（渠道不阻塞主流程）：{Title}", title);
        }
    }
}
