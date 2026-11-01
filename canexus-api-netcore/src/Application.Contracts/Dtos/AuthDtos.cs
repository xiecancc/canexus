
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

/// <summary>路由元信息（前端渲染菜单 / 页面缓存使用）。</summary>
public class RouteMeta
{
    /// <summary>菜单标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>菜单图标。</summary>
    public string? Icon { get; set; }

    /// <summary>页面是否缓存（KeepAlive）。</summary>
    public bool KeepAlive { get; set; }

    /// <summary>是否隐藏。</summary>
    public bool Hidden { get; set; }

    /// <summary>显示顺序。</summary>
    public int Order { get; set; }
}

/// <summary>前端路由视图对象（/api/auth/routes 响应数据，完整菜单树）。</summary>
public class RouteResponse
{
    /// <summary>路由名（驼峰，全局唯一）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>路由地址（顶层带 / 前缀，子级相对）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>组件路径（相对 views 目录，如 system/user/index）；目录为 null。</summary>
    public string? Component { get; set; }

    /// <summary>重定向地址（目录且有子级时为第一个子级完整路径）。</summary>
    public string? Redirect { get; set; }

    /// <summary>路由元信息。</summary>
    public RouteMeta Meta { get; set; } = new();

    /// <summary>子路由。</summary>
    public List<RouteResponse> Children { get; set; } = [];
}
