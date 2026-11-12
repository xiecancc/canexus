
using API.Extensions;
using API.Logging;
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using Infrastructure.Shared.Options;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using static API.Extensions.SecurityDependencyExtensions;

namespace API.Controllers;
/// <summary>认证授权接口：验证码、登录、刷新令牌、登出、用户信息与路由。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
/// <param name="jwtOptions">JWT 配置（refreshToken Cookie 可选模式）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/auth")]
public class AuthController(ISender sender, IOptionsMonitor<JwtOption> jwtOptions) : ControllerBase
{
    /// <summary>refreshToken Cookie 的作用路径（仅认证端点需要携带，避免扩散到全部接口）。</summary>
    private const string RefreshTokenCookiePath = "/api/auth";

    /// <summary>获取图形验证码（SVG base64，有效期 5 分钟，一次性校验）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>验证码 Id 与图片。</returns>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<IActionResult> Captcha(CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new AuthQueryCaptcha(), cancellationToken));
    }

    /// <summary>账号密码登录（验证码校验 + 失败锁定策略）。</summary>
    /// <param name="request">登录请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>令牌对（accessToken / refreshToken / expiresIn）。</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken = default)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var data = await sender.Send(new AuthLoginCommand(request, ip, userAgent), cancellationToken);
        WriteRefreshTokenCookie(data.RefreshToken);
        return this.OkData(data, "登录成功");
    }

    /// <summary>刷新令牌（轮换：旧 refreshToken 失效，发放新令牌对）。</summary>
    /// <param name="request">刷新请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>新令牌对。</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var refreshToken = ResolveRefreshToken(request.RefreshToken);
        var data = await sender.Send(new AuthRefreshTokenCommand(refreshToken, ip, userAgent), cancellationToken);
        WriteRefreshTokenCookie(data.RefreshToken);
        return this.OkData(data);
    }

    /// <summary>登出：吊销 refreshToken（不传时吊销当前用户全部活跃令牌）。</summary>
    /// <param name="request">登出请求（可空）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("logout")]
    [Authorize]
    [API.Authorization.AllowWhileMustChangePassword]
    [API.Authorization.AllowAnyAuthenticated]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken = default)
    {
        var refreshToken = ResolveRefreshToken(request?.RefreshToken);
        await sender.Send(new AuthLogoutCommand(string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken), cancellationToken);
        DeleteRefreshTokenCookie();
        return Ok(Result.OkMessage("退出成功"));
    }

    /// <summary>自助修改密码（校验原密码 → 更新哈希 → 清除强制改密标记 → 吊销全部刷新令牌强制重新登录；敏感凭证变更留痕审计）。</summary>
    /// <param name="request">修改密码请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("change-password")]
    [Authorize]
    [API.Authorization.AllowWhileMustChangePassword]
    [API.Authorization.AllowAnyAuthenticated]
    [OperLog("认证授权", OperBusinessType.Update)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new AuthChangePasswordCommand(request.OldPassword, request.NewPassword), cancellationToken);
        return this.OkData(affected, "密码修改成功，请重新登录");
    }

    /// <summary>获取当前登录用户信息（角色与权限标识，超管权限为 ["*:*:*"]）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>用户信息。</returns>
    [HttpGet("user-info")]
    [Authorize]
    [API.Authorization.AllowWhileMustChangePassword]
    [API.Authorization.AllowAnyAuthenticated]
    public async Task<IActionResult> UserInfo(CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new UserQueryInfo(), cancellationToken));
    }

    /// <summary>获取当前用户的前端路由树（按角色过滤，仅目录 / 菜单，排除按钮与隐藏项）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>路由树。</returns>
    [HttpGet("routes")]
    [Authorize]
    [API.Authorization.AllowWhileMustChangePassword]
    [API.Authorization.AllowAnyAuthenticated]
    public async Task<IActionResult> Routes(CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new UserQueryRoutes(), cancellationToken));
    }

    /// <summary>解析刷新令牌：请求体优先；开启 Cookie 模式且请求体为空时回退到 Cookie（前端平滑迁移）。</summary>
    /// <param name="bodyToken">请求体中的刷新令牌。</param>
    /// <returns>用于命令的刷新令牌；均无值时返回空串，由命令验证器统一拒绝。</returns>
    private string ResolveRefreshToken(string? bodyToken)
    {
        if (!string.IsNullOrWhiteSpace(bodyToken))
        {
            return bodyToken;
        }

        var option = jwtOptions.CurrentValue;
        return option.RefreshTokenCookieEnabled
            && Request.Cookies.TryGetValue(option.RefreshTokenCookieName, out var cookie)
            ? cookie ?? string.Empty
            : string.Empty;
    }

    /// <summary>按配置写入 refreshToken Cookie（关闭时为空操作，响应体契约不变）。</summary>
    /// <param name="refreshToken">新签发的刷新令牌。</param>
    private void WriteRefreshTokenCookie(string refreshToken)
    {
        var option = jwtOptions.CurrentValue;
        if (!option.RefreshTokenCookieEnabled)
        {
            return;
        }

        var cookieOptions = BuildCookieOptions();
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(option.RefreshTokenDays);
        Response.Cookies.Append(option.RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    /// <summary>登出时删除 refreshToken Cookie（须与写入使用同一 Path / SameSite 属性）。</summary>
    private void DeleteRefreshTokenCookie()
    {
        var option = jwtOptions.CurrentValue;
        if (!option.RefreshTokenCookieEnabled)
        {
            return;
        }

        Response.Cookies.Delete(option.RefreshTokenCookieName, BuildCookieOptions());
    }

    /// <summary>构造安全 Cookie 属性：HttpOnly 阻断脚本读取、Secure 限定 HTTPS、Strict 抵御跨站携带。</summary>
    /// <returns>Cookie 属性。</returns>
    private static CookieOptions BuildCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshTokenCookiePath,
        IsEssential = true
    };
}
