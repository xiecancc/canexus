
using API.Authorization;
using API.Extensions;
using Application.Contracts.Monitor;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>系统监控接口：服务器与进程运行信息（只读快照，供运维监控展示）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/monitor/server")]
[Authorize]
public class SysMonitorController(ISender sender) : ControllerBase
{
    /// <summary>获取服务器与进程运行信息（主机 / 运行时 / 内存 / GC / 线程 / 调度器概要）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>运行信息快照。</returns>
    [HttpGet]
    [RequirePermission(PermissionDefinitions.MonitorServerList)]
    public async Task<IActionResult> Server(CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new ServerMonitorQuery(), cancellationToken));
    }
}
