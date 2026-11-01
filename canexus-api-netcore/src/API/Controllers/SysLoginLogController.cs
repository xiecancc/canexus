
using API.Authorization;
using Application.Contracts.Commands;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>登录日志接口：分页查询与清空（写入由认证服务在登录链路自动完成）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/login-log")]
[Authorize]
public class SysLoginLogController(ISender sender) : ControllerBase
{
    /// <summary>分页查询登录日志（账号 / IP / 状态 / 时间范围，beginTime / endTime 格式 yyyy-MM-dd）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("monitor:logininfor:list")]
    public async Task<IActionResult> Page([FromQuery] LoginLogPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new LoginLogQueryPaged(request))));
    }

    /// <summary>清空全部登录日志（物理删除，危险操作）。</summary>
    /// <returns>统一响应。</returns>
    [HttpPost("clean")]
    [RequirePermission("monitor:logininfor:clean")]
    public async Task<IActionResult> Clean()
    {
        await sender.Send(new LoginLogCleanCommand());
        return Ok(Result.Ok(message: "清空成功"));
    }

    /// <summary>按 Id 删除单条登录日志（物理删除）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("monitor:logininfor:remove")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] LoginLogDeleteRequest request)
    {
        await sender.Send(new LoginLogDeleteCommand(id, request.Version));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
