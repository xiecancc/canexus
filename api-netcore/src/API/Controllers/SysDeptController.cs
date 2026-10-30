namespace API.Controllers;

using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Depts;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>部门管理接口：树查询、详情、增删改。</summary>
[ApiController]
[Route("api/system/dept")]
[Authorize]
public class SysDeptController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>构造函数。</summary>
    /// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
    public SysDeptController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>查询部门树（keyword 匹配部门名称 / 负责人；status 精确）。</summary>
    /// <param name="keyword">关键字。</param>
    /// <param name="status">状态。</param>
    /// <returns>部门树。</returns>
    [HttpGet("tree")]
    [RequirePermission("system:dept:list")]
    public async Task<IActionResult> Tree([FromQuery] string? keyword, [FromQuery] Domain.Shared.Enums.CommonStatus? status)
    {
        return Ok(Result.Ok(await _sender.Send(new GetDeptTreeQuery(keyword, status))));
    }

    /// <summary>获取部门详情。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>部门详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("system:dept:query")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await _sender.Send(new GetDeptDetailQuery(id))));
    }

    /// <summary>创建部门（父部门存在性由处理器校验）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新部门 Id。</returns>
    [HttpPost]
    [RequirePermission("system:dept:add")]
    [OperLog("部门管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DeptCreateRequest request)
    {
        var id = await _sender.Send(new CreateDeptCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
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
        await _sender.Send(new UpdateDeptCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除部门（存在子部门或部门下存在用户时拒绝）。</summary>
    /// <param name="id">部门 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:dept:remove")]
    [OperLog("部门管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteDeptCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
