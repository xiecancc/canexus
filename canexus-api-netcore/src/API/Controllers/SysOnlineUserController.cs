
using Asp.Versioning;
using API.Authorization;
using Application.Contracts.Commands;
using Application.Contracts.Online;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>在线会话接口：分页查询与强制下线（会话数据源为活跃 refreshToken 记录）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/monitor/online")]
[Authorize]
public class SysOnlineUserController(ISender sender) : ControllerBase
{
    /// <summary>分页查询在线会话（账号 / IP 过滤）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("monitor:online:list")]
    public async Task<IActionResult> Page([FromQuery] OnlineUserPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new OnlineUserQueryPaged(request))));
    }

    /// <summary>强制下线指定在线会话（吊销对应 refreshToken，会话将无法继续刷新令牌）。</summary>
    /// <param name="tokenId">在线会话记录 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("{tokenId:guid}/revoke")]
    [RequirePermission("monitor:online:forceLogout")]
    public async Task<IActionResult> ForceLogout(Guid tokenId)
    {
        await sender.Send(new OnlineForceLogoutCommand(tokenId));
        return Ok(Result.Ok(message: "强制下线成功"));
    }
}
