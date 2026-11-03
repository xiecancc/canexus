namespace Infrastructure.Shared.Options;

/// <summary>
/// 邮件通知配置选项
/// <para>SMTP 投递参数；Enabled=false 时邮件渠道整体跳过（不建立连接）。
/// Password 为 SMTP 授权码（非邮箱登录密码），生产环境经环境变量 Mail__Password 注入，不得入库入仓。</para>
/// </summary>
public class MailOption : OptionBase
{
    /// <summary>邮件渠道总开关。</summary>
    public bool Enabled { get; set; }

    /// <summary>SMTP 服务器地址。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP 端口（587 STARTTLS / 465 SSL / 25 明文）。</summary>
    public int Port { get; set; } = 587;

    /// <summary>发件人地址（须与 SMTP 账号一致或为其授权发件域）。</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>SMTP 账号（通常与发件人地址相同）。</summary>
    public string? Username { get; set; }

    /// <summary>SMTP 授权码 / 密码（生产环境经环境变量注入）。</summary>
    public string? Password { get; set; }

    /// <summary>是否启用 SSL/TLS（465 端口通常为 true，587 通常配合 STARTTLS 视服务方要求）。</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>公告投递收件人列表（当前实现为固定管理员广播组，按用户订阅扩展开启后走动态收件人）。</summary>
    public List<string> Recipients { get; set; } = [];

    /// <summary>投递超时（毫秒，防 SMTP 服务无响应拖垮公告创建）。</summary>
    public int TimeoutMilliseconds { get; set; } = 10000;

    /// <inheritdoc />
    public override void Validate()
    {
        if (!Enabled)
        {
            // 渠道关闭时其余字段不参与校验（允许留空占位）
            return;
        }

        EnsureNotNullOrWhiteSpace(Host, nameof(Host), "Mail:Host 不能为空（邮件渠道已启用）");
        EnsureInRange(Port, 1, 65535, nameof(Port), "Mail:Port 必须在 1-65535 之间");
        EnsureNotNullOrWhiteSpace(From, nameof(From), "Mail:From 不能为空（邮件渠道已启用）");
        EnsureNotEmpty(Recipients, nameof(Recipients), "Mail:Recipients 不能为空集合（邮件渠道已启用）");
        EnsureGreaterThanZero(TimeoutMilliseconds, nameof(TimeoutMilliseconds), "Mail:TimeoutMilliseconds 必须大于 0");

        foreach (var recipient in Recipients)
        {
            Ensure(
                !string.IsNullOrWhiteSpace(recipient) && recipient.Contains('@'),
                nameof(Recipients),
                $"Mail:Recipients 中存在非法邮箱地址：'{recipient}'");
        }
    }
}
