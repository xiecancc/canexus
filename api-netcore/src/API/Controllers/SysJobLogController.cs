namespace API.Controllers;

using API.Authorization;
using Application.Contracts.Jobs;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>定时任务执行日志接口：分页查询（按触发时间倒序，最新在前）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/monitor/job-log")]
[Authorize]
public class SysJobLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询任务执行日志（jobId / status 精确过滤，倒序）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("monitor:job:query")]
    public async Task<IActionResult> Page([FromQuery] JobLogPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new GetJobLogPageQuery(request))));
    }
}
