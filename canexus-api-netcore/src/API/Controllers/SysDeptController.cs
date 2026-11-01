
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Application.Contracts.Queries;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>部门管理接口：树查询、详情、增删改。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/dept")]
[Authorize]
public class SysDeptController(ISender sender) : ControllerBase
{
    /// <summary>查询部门树（keyword 匹配部门名称 / 负责人；status 精确）。</summary>
    /// <param name="keyword">关键字。</param>
    /// <param name="status">状态。</param>
    /// <returns>部门树。</returns>
    [HttpGet("tree")]
    [RequirePermission("system:dept:list")]
    public async Task<IActionResult> Tree([FromQuery] string? keyword, [FromQuery] Domain.Shared.Enums.CommonStatus? status)
    {
        return Ok(Result.Ok(await sender.Send(new DeptQueryTree(keyword, status))));
    }

    /// <summary>获取部门详情。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>部门详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("system:dept:query")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await sender.Send(new DeptQueryDetail(id))));
    }

    /// <summary>创建部门（父部门存在性由处理器校验）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新部门 Id。</returns>
    [HttpPost]
    [RequirePermission("system:dept:add")]
    [OperLog("部门管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DeptCreateRequest request)
    {
        await sender.Send(new DeptCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>更新部门（父级不可选自身或自己的后代）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:dept:edit")]
    [OperLog("部门管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] DeptUpdateRequest request)
    {
        await sender.Send(new DeptUpdateCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除部门（存在子部门或部门下存在用户时拒绝）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="request">删除请求（含乐观锁版本号）。</param>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:dept:remove")]
    [OperLog("部门管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DeptDeleteRequest request)
    {
        await sender.Send(new DeptDeleteCommand(id, request.Version));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
