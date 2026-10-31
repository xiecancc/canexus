
using API.Authorization;
using Application.Contracts.Commands;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>操作日志接口：分页查询与清空（写入由 [OperLog] 特性 + 全局过滤器自动完成）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/oper-log")]
[Authorize]
public class SysOperLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询操作日志（模块 / 业务类型 / 状态 / 操作人 / 时间范围，beginTime / endTime 格式 yyyy-MM-dd）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("monitor:operlog:list")]
    public async Task<IActionResult> Page([FromQuery] OperLogPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new GetOperLogPageQuery(request))));
    }

    /// <summary>清空全部操作日志（物理删除，危险操作；自身不记录操作日志，保证清空后列表为空）。</summary>
    /// <returns>统一响应。</returns>
    [HttpPost("clean")]
    [RequirePermission("monitor:operlog:clean")]
    public async Task<IActionResult> Clean()
    {
        await sender.Send(new CleanOperLogCommand());
        return Ok(Result.Ok(message: "清空成功"));
    }

    /// <summary>按 Id 删除单条操作日志（物理删除）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("monitor:operlog:remove")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await sender.Send(new DeleteOperLogCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
