
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Tenants;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>租户管理接口：分页、启用项简易列表、增删改与恢复（tenantKey 唯一且不可变更；存在归属用户拒绝删除）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/tenant")]
[Authorize]
public class SysTenantController(ISender sender) : ControllerBase
{
    /// <summary>分页查询租户（keyword 模糊匹配租户名称 / 租户标识；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:tenant:list")]
    public async Task<IActionResult> Page([FromQuery] TenantPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new TenantQueryPaged(request))));
    }

    /// <summary>查询启用状态的租户简易列表（下拉框等场景）。</summary>
    /// <returns>启用租户列表。</returns>
    [HttpGet("all")]
    [RequirePermission("system:tenant:list")]
    public async Task<IActionResult> All()
    {
        return Ok(Result.Ok(await sender.Send(new TenantQueryDropdown())));
    }

    /// <summary>创建租户（tenantKey 唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新租户 Id。</returns>
    [HttpPost]
    [RequirePermission("system:tenant:add")]
    [OperLog("租户管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] TenantInputRequest request)
    {
        await sender.Send(new TenantCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>更新租户（tenantKey 不可变更）。</summary>
    /// <param name="id">租户 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission("system:tenant:edit")]
    [OperLog("租户管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] TenantInputRequest request)
    {
        await sender.Send(new TenantUpdateCommand(id, version, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除租户（存在归属用户时拒绝删除）。</summary>
    /// <param name="id">租户 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission("system:tenant:remove")]
    [OperLog("租户管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version)
    {
        await sender.Send(new TenantDeleteCommand(id, version));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>恢复租户（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">租户 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission("system:tenant:restore")]
    [OperLog("租户管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version)
    {
        var restored = await sender.Send(new TenantRestoreCommand(id, version));
        return Ok(Result.Ok(message: restored ? "恢复成功" : "租户不存在或未删除，无需恢复"));
    }
}
