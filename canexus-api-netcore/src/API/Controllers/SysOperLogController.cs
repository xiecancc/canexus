
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>操作日志接口：分页查询与清空（写入由 [OperLog] 特性 + 全局过滤器自动完成）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/oper-log")]
[Authorize]
public class SysOperLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询操作日志（模块 / 业务类型 / 状态 / 操作人 / 时间范围，beginTime / endTime 格式 yyyy-MM-dd）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.MonitorOperlogList)]
    public async Task<IActionResult> Page([FromQuery] OperLogPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new OperLogQueryPaged(request))));
    }

    /// <summary>清空全部操作日志（物理删除，危险操作；清空动作本身经 [OperLog] 留痕——本条日志在清空后写入，
    /// 保证"谁在何时清空了日志"始终可追溯）。</summary>
    /// <returns>统一响应。</returns>
    [HttpPost("clean")]
    [RequirePermission(PermissionDefinitions.MonitorOperlogClean)]
    [OperLog("操作日志", OperBusinessType.Clean)]
    public async Task<IActionResult> Clean()
    {
        await sender.Send(new OperLogCleanCommand());
        return Ok(Result.Ok(message: "清空成功"));
    }

    /// <summary>按 Id 删除单条操作日志（物理删除）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.MonitorOperlogRemove)]
    [OperLog("操作日志", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version)
    {
        await sender.Send(new OperLogDeleteCommand(id, version));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>导出操作日志（xlsx；复用列表同口径筛选，行数上限 10000）。</summary>
    /// <param name="request">操作日志筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
    /// <returns>Excel 文件流。</returns>
    [HttpGet("export")]
    [RequirePermission(PermissionDefinitions.MonitorOperlogExport)]
    [OperLog("操作日志", OperBusinessType.Export)]
    public async Task<IActionResult> Export([FromQuery] OperLogPageRequest request)
    {
        var file = await sender.Send(new OperLogExportQuery(request));
        return File(file.Content, file.ContentType, file.FileName);
    }

}
