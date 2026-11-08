
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Posts;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>岗位管理接口：分页、详情、下拉、增删改与恢复（字段规则由应用层校验）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/post")]
[Authorize]
public class SysPostController(ISender sender) : ControllerBase
{
    /// <summary>分页查询岗位（名称 / 编码模糊、状态精确，显示顺序升序）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemPostList)]
    public async Task<IActionResult> Page([FromQuery] PostPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new PostQueryPaged(request), cancellationToken)));
    }

    /// <summary>岗位下拉查询（仅启用岗位，显示顺序升序；供用户管理页岗位分配）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>下拉简项列表。</returns>
    [HttpGet("dropdown")]
    [RequirePermission(PermissionDefinitions.SystemUserList)]
    public async Task<IActionResult> Dropdown(CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new PostQueryDropdown(), cancellationToken)));
    }

    /// <summary>按 Id 查询岗位详情。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>岗位详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemPostQuery)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new PostQueryDetail(id), cancellationToken)));
    }

    /// <summary>创建岗位。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示岗位写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemPostAdd)]
    [OperLog("岗位管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] PostInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new PostCreateCommand(request), cancellationToken);
        return Ok(Result.Ok(affected, message: "创建成功"));
    }

    /// <summary>更新岗位（校验规则与创建一致）。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemPostEdit)]
    [OperLog("岗位管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] PostInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new PostUpdateCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "更新成功"));
    }

    /// <summary>删除岗位（岗位仍被用户引用时返回 401304 拒绝删除）。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemPostRemove)]
    [OperLog("岗位管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new PostDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }

    /// <summary>恢复岗位（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemPostRestore)]
    [OperLog("岗位管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new PostRestoreCommand(id, version), cancellationToken);
        return Ok(Result.Ok(restored, message: restored ? "恢复成功" : "岗位不存在或未删除，无需恢复"));
    }
}
