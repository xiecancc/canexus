
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Online;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>在线会话接口：分页查询与强制下线（会话数据源为活跃 refreshToken 记录）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/monitor/online")]
[Authorize]
public class SysOnlineUserController(ISender sender) : ControllerBase
{
    /// <summary>分页查询在线会话（账号 / IP 过滤）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.MonitorOnlineList)]
    public async Task<IActionResult> Page([FromQuery] OnlineUserPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new OnlineUserQueryPaged(request), cancellationToken)));
    }

    /// <summary>强制下线指定在线会话（吊销对应 refreshToken，会话将无法继续刷新令牌；高危操作留痕审计）。</summary>
    /// <param name="tokenId">在线会话记录 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("{tokenId:guid}/revoke")]
    [RequirePermission(PermissionDefinitions.MonitorOnlineForceLogout)]
    [OperLog("在线会话", OperBusinessType.Force)]
    public async Task<IActionResult> ForceLogout(Guid tokenId, CancellationToken cancellationToken = default)
    {
        await sender.Send(new OnlineForceLogoutCommand(tokenId), cancellationToken);
        return Ok(Result.OkMessage("强制下线成功"));
    }
}
