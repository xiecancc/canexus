
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>部门管理接口：树查询、详情、增删改与恢复。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/dept")]
[Authorize]
public class SysDeptController(ISender sender) : ControllerBase
{
    /// <summary>查询部门树（keyword 匹配部门名称 / 负责人；status 精确）。</summary>
    /// <param name="keyword">关键字。</param>
    /// <param name="status">状态。</param>
    /// <returns>部门树。</returns>
    [HttpGet("tree")]
    [RequirePermission(PermissionDefinitions.SystemDeptList)]
    public async Task<IActionResult> Tree([FromQuery] string? keyword, [FromQuery] Domain.Shared.Enums.CommonStatus? status)
    {
        return Ok(Result.Ok(await sender.Send(new DeptQueryTree(keyword, status))));
    }

    /// <summary>获取部门详情。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>部门详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDeptQuery)]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await sender.Send(new DeptQueryDetail(id))));
    }

    /// <summary>创建部门（父部门存在性由处理器校验）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新部门 Id。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemDeptAdd)]
    [OperLog("部门管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DeptInputRequest request)
    {
        await sender.Send(new DeptCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>更新部门（父级不可选自身或自己的后代）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDeptEdit)]
    [OperLog("部门管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] DeptInputRequest request)
    {
        await sender.Send(new DeptUpdateCommand(id, version, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除部门（存在子部门或部门下存在用户时拒绝）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDeptRemove)]
    [OperLog("部门管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version)
    {
        await sender.Send(new DeptDeleteCommand(id, version));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>恢复部门（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemDeptRestore)]
    [OperLog("部门管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version)
    {
        var restored = await sender.Send(new DeptRestoreCommand(id, version));
        return Ok(Result.Ok(message: restored ? "恢复成功" : "部门不存在或未删除，无需恢复"));
    }
}
