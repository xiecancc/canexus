
using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Auth;
/// <summary>登录请求。</summary>
public class LoginRequest
{
    /// <summary>用户名。</summary>
    [Required(ErrorMessage = "用户名不能为空")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码（明文传输，依赖 HTTPS）。</summary>
    [Required(ErrorMessage = "密码不能为空")]
    public string Password { get; set; } = string.Empty;

    /// <summary>验证码 Id（由 /api/auth/captcha 返回）。</summary>
    [Required(ErrorMessage = "验证码 Id 不能为空")]
    public string CaptchaId { get; set; } = string.Empty;

    /// <summary>验证码内容（忽略大小写）。</summary>
    [Required(ErrorMessage = "验证码不能为空")]
    public string CaptchaCode { get; set; } = string.Empty;
}

/// <summary>刷新令牌请求。</summary>
public class RefreshRequest
{
    /// <summary>刷新令牌。</summary>
    [Required(ErrorMessage = "刷新令牌不能为空")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>登出请求（刷新令牌可空：不传时吊销当前用户全部活跃刷新令牌）。</summary>
public class LogoutRequest
{
    /// <summary>刷新令牌；为空时吊销当前用户全部活跃刷新令牌。</summary>
    public string? RefreshToken { get; set; }
}
