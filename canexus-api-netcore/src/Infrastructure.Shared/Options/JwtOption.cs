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

    /// <summary>accessToken 有效期（分钟）。</summary>
    public int AccessTokenMinutes { get; set; } = 120;

    /// <summary>refreshToken 有效期（天）。</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>签名密钥（HS256，长度不小于 32 字符）。</summary>
    public string Secret { get; set; } = string.Empty;

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureNotNullOrWhiteSpace(Issuer, nameof(Issuer), "Jwt:Issuer 不能为空");
        EnsureNotNullOrWhiteSpace(Audience, nameof(Audience), "Jwt:Audience 不能为空");
        EnsureInRange(AccessTokenMinutes, 1, 1_440, nameof(AccessTokenMinutes), "AccessTokenMinutes 必须在 1-1440 分钟（一天）之间");
        EnsureInRange(RefreshTokenDays, 1, 365, nameof(RefreshTokenDays), "RefreshTokenDays 必须在 1-365 天之间");
        EnsureNotNullOrWhiteSpace(Secret, nameof(Secret), "Jwt:Secret 不能为空");
        Ensure(Secret != InsecureDefaultSecret, nameof(Secret),
            $"Jwt:Secret 不能使用默认不安全值 {InsecureDefaultSecret}，请配置独立的随机密钥");
        EnsureMinLength(Secret, 32, nameof(Secret), "Jwt:Secret 长度不得小于 32 字符");
    }
}
