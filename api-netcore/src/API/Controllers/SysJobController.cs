namespace API.Controllers;

using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Jobs;
using API.Authorization;
using API.Logging;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>定时任务管理接口：分页、增删改、启停与立即执行（创建 / 修改 / 启停即时同步 Quartz 调度器）。</summary>
[ApiController]
[Route("api/monitor/job")]
[Authorize]
public class SysJobController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>构造函数。</summary>
    /// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
    public SysJobController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>分页查询定时任务（keyword 模糊匹配任务名称 / 任务分组；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("monitor:job:list")]
    public async Task<IActionResult> Page([FromQuery] JobPageRequest request)
    {
        return Ok(Result.Ok(await _sender.Send(new GetJobPageQuery(request))));
    }

    /// <summary>创建定时任务（Cron 与任务类型校验；status=1 时创建后立即入调度）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新任务 Id。</returns>
    [HttpPost]
    [RequirePermission("monitor:job:add")]
    [OperLog("定时任务", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] JobCreateRequest request)
    {
        var id = await _sender.Send(new CreateJobCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
    }

    /// <summary>更新定时任务（按新定义重调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("monitor:job:edit")]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] JobUpdateRequest request)
    {
        await _sender.Send(new UpdateJobCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除定时任务（同时移出调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("monitor:job:remove")]
    [OperLog("定时任务", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteJobCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>修改任务状态（1 运行入调度，0 暂停移出调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="request">状态请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/status")]
    [RequirePermission("monitor:job:changeStatus")]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] JobStatusRequest request)
    {
        await _sender.Send(new UpdateJobStatusCommand(id, request));
        return Ok(Result.Ok(message: "状态更新成功"));
    }

    /// <summary>立即触发一次执行（不影响既定 Cron 调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/run")]
    [RequirePermission("monitor:job:run")]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> RunOnce(Guid id)
    {
        await _sender.Send(new RunJobOnceCommand(id));
        return Ok(Result.Ok(message: "执行成功，执行结果请在执行日志中查看"));
    }
}
