namespace Infrastructure.Shared.Options;

/// <summary>HTTPS 安全传输配置项（绑定配置节 Https）。</summary>
public sealed class HttpsOption : OptionBase
{
    /// <summary>是否将 HTTP 请求强制重定向到 HTTPS（需宿主已配置 HTTPS 证书；本地开发默认关闭）。</summary>
    public bool RedirectEnabled { get; set; }

    /// <summary>重定向目标 HTTPS 端口（0 = 使用 ASP.NET Core 默认解析，如 ASPNETCORE_HTTPS_PORT / launchSettings）。</summary>
    public int HttpsPort { get; set; }

    /// <summary>是否启用 HSTS（强制浏览器后续访问走 HTTPS；仅非 Development 环境生效）。</summary>
    public bool HstsEnabled { get; set; }

    /// <summary>HSTS max-age 时长（天）。</summary>
    public int HstsMaxAgeDays { get; set; } = 180;

    /// <inheritdoc />
    public override void Validate()
    {
        Ensure(HttpsPort is 0 or >= 1 and <= 65_535, nameof(HttpsPort), "Https:HttpsPort 必须为 0（默认解析）或 1-65535");
        Ensure(HstsMaxAgeDays >= 1 && HstsMaxAgeDays <= 730, nameof(HstsMaxAgeDays), "Https:HstsMaxAgeDays 必须在 1-730 天之间");
    }
}
