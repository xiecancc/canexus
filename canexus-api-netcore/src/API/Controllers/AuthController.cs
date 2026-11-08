
using API.Logging;
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static API.Extensions.SecurityDependencyExtensions;

namespace API.Controllers;
/// <summary>认证授权接口：验证码、登录、刷新令牌、登出、用户信息与路由。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/auth")]
public class AuthController(ISender sender) : ControllerBase
{
    /// <summary>获取图形验证码（SVG base64，有效期 5 分钟，一次性校验）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>验证码 Id 与图片。</returns>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<IActionResult> Captcha(CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new AuthQueryCaptcha(), cancellationToken)));
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
        return Ok(Result.Ok(data, "登录成功"));
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
        var data = await sender.Send(new AuthRefreshTokenCommand(request.RefreshToken, ip, userAgent), cancellationToken);
        return Ok(Result.Ok(data));
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
        await sender.Send(new AuthLogoutCommand(request?.RefreshToken), cancellationToken);
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
        return Ok(Result.Ok(affected, message: "密码修改成功，请重新登录"));
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
        return Ok(Result.Ok(await sender.Send(new UserQueryInfo(), cancellationToken)));
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
        return Ok(Result.Ok(await sender.Send(new UserQueryRoutes(), cancellationToken)));
    }
}
