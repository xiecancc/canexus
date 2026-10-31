
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>认证授权接口：验证码、登录、刷新令牌、登出、用户信息与路由。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/auth")]
public class AuthController(ISender sender) : ControllerBase
{
    /// <summary>获取图形验证码（SVG base64，有效期 5 分钟，一次性校验）。</summary>
    /// <returns>验证码 Id 与图片。</returns>
    [HttpGet("captcha")]
    [AllowAnonymous]
    public async Task<IActionResult> Captcha()
    {
        return Ok(Result.Ok(await sender.Send(new GetCaptchaQuery())));
    }

    /// <summary>账号密码登录（验证码校验 + 失败锁定策略）。</summary>
    /// <param name="request">登录请求。</param>
    /// <returns>令牌对（accessToken / refreshToken / expiresIn）。</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var data = await sender.Send(new LoginCommand(request, ip, userAgent));
        return Ok(Result.Ok(data, "登录成功"));
    }

    /// <summary>刷新令牌（轮换：旧 refreshToken 失效，发放新令牌对）。</summary>
    /// <param name="request">刷新请求。</param>
    /// <returns>新令牌对。</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var data = await sender.Send(new RefreshTokenCommand(request.RefreshToken));
        return Ok(Result.Ok(data));
    }

    /// <summary>登出：吊销 refreshToken（不传时吊销当前用户全部活跃令牌）。</summary>
    /// <param name="request">登出请求（可空）。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request)
    {
        await sender.Send(new LogoutCommand(request?.RefreshToken));
        return Ok(Result.Ok(message: "退出成功"));
    }

    /// <summary>获取当前登录用户信息（角色与权限标识，超管权限为 ["*:*:*"]）。</summary>
    /// <returns>用户信息。</returns>
    [HttpGet("user-info")]
    [Authorize]
    public async Task<IActionResult> UserInfo()
    {
        return Ok(Result.Ok(await sender.Send(new GetUserInfoQuery())));
    }

    /// <summary>获取当前用户的前端路由树（按角色过滤，仅目录 / 菜单，排除按钮与隐藏项）。</summary>
    /// <returns>路由树。</returns>
    [HttpGet("routes")]
    [Authorize]
    public async Task<IActionResult> Routes()
    {
        return Ok(Result.Ok(await sender.Send(new GetRoutesQuery())));
    }
}
