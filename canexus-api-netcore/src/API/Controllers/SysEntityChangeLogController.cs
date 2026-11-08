
using API.Authorization;
using Application.Contracts.Audit;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>实体变更日志接口：分页查询（写入由聚合仓储更新链路自动完成）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/entity-change")]
[Authorize]
public class SysEntityChangeLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询实体变更日志（实体类型名 / 实体 Id / 字段名过滤）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.MonitorChangelogList)]
    public async Task<IActionResult> Page([FromQuery] EntityChangeLogPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new EntityChangeLogQueryPaged(request), cancellationToken)));
    }
}
