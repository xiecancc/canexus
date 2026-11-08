
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Jobs;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>定时任务管理接口：分页、增删改与恢复、启停与立即执行（创建 / 修改 / 启停 / 恢复即时同步 Quartz 调度器）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/monitor/job")]
[Authorize]
public class SysJobController(ISender sender) : ControllerBase
{
    /// <summary>分页查询定时任务（keyword 模糊匹配任务名称 / 任务分组；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.MonitorJobList)]
    public async Task<IActionResult> Page([FromQuery] JobPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new JobQueryPaged(request), cancellationToken)));
    }

    /// <summary>创建定时任务（Cron 与任务类型校验；status=1 时创建后立即入调度）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示任务写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.MonitorJobAdd)]
    [OperLog("定时任务", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] JobInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new JobCreateCommand(request), cancellationToken);
        return Ok(Result.Ok(affected, message: "创建成功"));
    }

    /// <summary>更新定时任务（按新定义重调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.MonitorJobEdit)]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] JobInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new JobUpdateCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "更新成功"));
    }

    /// <summary>删除定时任务（同时移出调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.MonitorJobRemove)]
    [OperLog("定时任务", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new JobDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }

    /// <summary>恢复定时任务（解除软删除墓碑；启用状态任务重新入调度。幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.MonitorJobRestore)]
    [OperLog("定时任务", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new JobRestoreCommand(id, version), cancellationToken);
        return Ok(Result.Ok(restored, message: restored ? "恢复成功" : "任务不存在或未删除，无需恢复"));
    }

    /// <summary>修改任务状态（1 运行入调度，0 暂停移出调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="request">状态请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/status")]
    [RequirePermission(PermissionDefinitions.MonitorJobChangeStatus)]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> UpdateStatus(Guid id, Guid version, [FromBody] JobStatusRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new JobUpdateStatusCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "状态更新成功"));
    }

    /// <summary>立即触发一次执行（不影响既定 Cron 调度）。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPost("{id:guid}/run")]
    [RequirePermission(PermissionDefinitions.MonitorJobRun)]
    [OperLog("定时任务", OperBusinessType.Update)]
    public async Task<IActionResult> RunOnce(Guid id, CancellationToken cancellationToken = default)
    {
        await sender.Send(new JobRunOnceCommand(id), cancellationToken);
        return Ok(Result.OkMessage("执行成功，执行结果请在执行日志中查看"));
    }
}
