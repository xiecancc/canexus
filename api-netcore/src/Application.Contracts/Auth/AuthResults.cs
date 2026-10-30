namespace Application.Contracts.Auth;

/// <summary>登录 / 刷新结果：令牌对与有效期。</summary>
public class TokenPairDto
{
    /// <summary>访问令牌（JWT）。</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>刷新令牌。</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>访问令牌有效期（秒）。</summary>
    public long ExpiresIn { get; set; }
}

/// <summary>图形验证码结果。</summary>
public class CaptchaResult
{
    /// <summary>验证码 Id（登录时回传）。</summary>
    public string CaptchaId { get; set; } = string.Empty;

    /// <summary>验证码图片（SVG base64，格式：data:image/svg+xml;base64,...）。</summary>
    public string ImageBase64 { get; set; } = string.Empty;
}

/// <summary>当前用户信息（/api/auth/user-info 响应数据）。</summary>
public class UserInfoResult
{
    /// <summary>用户 Id。</summary>
    public Guid UserId { get; set; }

    /// <summary>用户名。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>昵称。</summary>
    public string NickName { get; set; } = string.Empty;

    /// <summary>头像地址。</summary>
    public string? Avatar { get; set; }

    /// <summary>角色权限字符串集合（roleKey）。</summary>
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    /// <summary>权限标识集合；超管固定为 ["*:*:*"]。</summary>
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}
