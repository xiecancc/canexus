
using Asp.Versioning;
using API.Authorization;
using Application.Contracts.Monitor;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>系统监控接口：服务器与进程运行信息（只读快照，供运维监控展示）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/monitor/server")]
[Authorize]
public class SysMonitorController(ISender sender) : ControllerBase
{
    /// <summary>获取服务器与进程运行信息（主机 / 运行时 / 内存 / GC / 线程 / 调度器概要）。</summary>
    /// <returns>运行信息快照。</returns>
    [HttpGet]
    [RequirePermission("monitor:server:list")]
    public async Task<IActionResult> Server()
    {
        return Ok(Result.Ok(await sender.Send(new ServerMonitorQuery())));
    }
}
