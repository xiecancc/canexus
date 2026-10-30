namespace API.Controllers;

using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Tenants;
using API.Authorization;
using API.Logging;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>租户管理接口：分页、启用项简易列表、增删改（tenantKey 唯一且不可变更；存在归属用户拒绝删除）。</summary>
[ApiController]
[Route("api/system/tenant")]
[Authorize]
public class SysTenantController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>构造函数。</summary>
    /// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
    public SysTenantController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>分页查询租户（keyword 模糊匹配租户名称 / 租户标识；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:tenant:list")]
    public async Task<IActionResult> Page([FromQuery] TenantPageRequest request)
    {
        return Ok(Result.Ok(await _sender.Send(new GetTenantPageQuery(request))));
    }

    /// <summary>查询启用状态的租户简易列表（下拉框等场景）。</summary>
    /// <returns>启用租户列表。</returns>
    [HttpGet("all")]
    [RequirePermission("system:tenant:list")]
    public async Task<IActionResult> All()
    {
        return Ok(Result.Ok(await _sender.Send(new GetAllTenantsQuery())));
    }

    /// <summary>创建租户（tenantKey 唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新租户 Id。</returns>
    [HttpPost]
    [RequirePermission("system:tenant:add")]
    [OperLog("租户管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] TenantCreateRequest request)
    {
        var id = await _sender.Send(new CreateTenantCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
    }

    /// <summary>更新租户（tenantKey 不可变更）。</summary>
    /// <param name="id">租户 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:tenant:edit")]
    [OperLog("租户管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] TenantUpdateRequest request)
    {
        await _sender.Send(new UpdateTenantCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除租户（存在归属用户时拒绝删除）。</summary>
    /// <param name="id">租户 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:tenant:remove")]
    [OperLog("租户管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteTenantCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
