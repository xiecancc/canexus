using System.Text;

namespace Infrastructure.Shared.Options;

/// <summary>JWT 认证配置项（绑定配置节 Jwt）。</summary>
public sealed class JwtOption : OptionBase
{
    /// <summary>示例 / 历史模板中的不安全默认签名密钥，任何环境均禁止使用。</summary>
    public const string InsecureDefaultSecret = "change-me-in-production";

    /// <summary>Development 环境缺省回退密钥（仅本机调试使用，生产环境禁止依赖）。</summary>
    public const string DevelopmentFallbackSecret = "dev-only-insecure-jwt-secret-please-override-locally";

    /// <summary>签发者。</summary>
    public string Issuer { get; set; } = "SuperAdmin";

    /// <summary>受众。</summary>
    public string Audience { get; set; } = "SuperAdmin";

    /// <summary>accessToken 有效期（分钟）：短时令牌，限制改密 / 停用后的旧令牌可用窗口。</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>refreshToken 有效期（天）。</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>
    /// 是否额外把 refreshToken 写入 HttpOnly + Secure + SameSite=Strict Cookie（默认关闭）。
    /// <para>关闭时维持既有契约（前端从响应体取令牌并自行存储）；开启后前端可逐步迁移为
    /// 「不落 localStorage、刷新/登出由浏览器自动携带 Cookie」，响应体仍返回 refreshToken 以保持兼容。</para>
    /// </summary>
    public bool RefreshTokenCookieEnabled { get; set; }

    /// <summary>refreshToken Cookie 名称（仅在 <see cref="RefreshTokenCookieEnabled"/> 开启时使用）。</summary>
    public string RefreshTokenCookieName { get; set; } = "super-admin-refresh";

    /// <summary>签名密钥（HS256；按 UTF-8 编码后的字节数不少于 32 字节）。</summary>
    public string Secret { get; set; } = string.Empty;

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureNotNullOrWhiteSpace(Issuer, nameof(Issuer), "Jwt:Issuer 不能为空");
        EnsureNotNullOrWhiteSpace(Audience, nameof(Audience), "Jwt:Audience 不能为空");
        EnsureInRange(AccessTokenMinutes, 15, 30, nameof(AccessTokenMinutes), "AccessTokenMinutes 必须在 15-30 分钟之间（短时令牌）");
        EnsureInRange(RefreshTokenDays, 1, 365, nameof(RefreshTokenDays), "RefreshTokenDays 必须在 1-365 天之间");
        if (RefreshTokenCookieEnabled)
        {
            EnsureNotNullOrWhiteSpace(RefreshTokenCookieName, nameof(RefreshTokenCookieName),
                "启用 refreshToken Cookie 时 Jwt:RefreshTokenCookieName 不能为空");
        }
        EnsureNotNullOrWhiteSpace(Secret, nameof(Secret), "Jwt:Secret 不能为空");
        Ensure(Secret != InsecureDefaultSecret, nameof(Secret),
            $"Jwt:Secret 不能使用默认不安全值 {InsecureDefaultSecret}，请配置独立的随机密钥");
        // 验签密钥实际取 Encoding.UTF8.GetBytes(Secret)，按字节数而非字符数校验（HS256 要求 ≥256 位）
        Ensure(Encoding.UTF8.GetByteCount(Secret) >= 32, nameof(Secret),
            "Jwt:Secret 按 UTF-8 编码后不得少于 32 字节，请配置独立的随机密钥");
    }
}
