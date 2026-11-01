namespace Infrastructure.Shared.Options;

/// <summary>JWT 认证配置项（绑定配置节 Jwt）。</summary>
public sealed class JwtOption : OptionBase
{
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
        EnsureMinLength(Secret, 32, nameof(Secret), "Jwt:Secret 长度不得小于 32 字符");
    }
}
