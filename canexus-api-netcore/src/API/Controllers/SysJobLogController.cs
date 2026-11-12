
using API.Authorization;
using API.Extensions;
using Application.Contracts.Jobs;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>定时任务执行日志接口：分页查询（按触发时间倒序，最新在前）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/monitor/job-log")]
[Authorize]
public class SysJobLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询任务执行日志（jobId / status 精确过滤，倒序）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.MonitorJobQuery)]
    public async Task<IActionResult> Page([FromQuery] JobLogPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new JobLogQueryPaged(request), cancellationToken)));
    }
}
