
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>角色管理接口：分页、下拉简项、详情、增删改与恢复、菜单授权。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/role")]
[Authorize]
public class SysRoleController(ISender sender) : ControllerBase
{
    /// <summary>分页查询角色（keyword 匹配角色名称 / 角色权限字符；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:role:list")]
    public async Task<IActionResult> Page([FromQuery] RolePageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryPaged(request))));
    }

    /// <summary>获取全部启用角色（不分页，含超管角色，用于用户分配角色下拉）。</summary>
    /// <returns>角色简项列表。</returns>
    [HttpGet("all")]
    [RequirePermission("system:role:list")]
    public async Task<IActionResult> All()
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryDropdown())));
    }

    /// <summary>获取角色详情（含授权菜单 Id 集合，前端用作菜单授权回显）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <returns>角色详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("system:role:query")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryDetail(id))));
    }

    /// <summary>创建角色（roleKey 唯一且不可使用保留值 super_admin；菜单授权全删全插）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新角色 Id。</returns>
    [HttpPost]
    [RequirePermission("system:role:add")]
    [OperLog("角色管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] RoleInputRequest request)
    {
        await sender.Send(new RoleCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>更新角色（内置 super_admin 角色整体拒绝编辑）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission("system:role:edit")]
    [OperLog("角色管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] RoleInputRequest request)
    {
        await sender.Send(new RoleUpdateCommand(id, version, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除角色（内置 super_admin 角色拒绝删除；已被用户引用拒绝删除）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission("system:role:remove")]
    [OperLog("角色管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version)
    {
        await sender.Send(new RoleDeleteCommand(id, version));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>恢复角色（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission("system:role:restore")]
    [OperLog("角色管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version)
    {
        var restored = await sender.Send(new RoleRestoreCommand(id, version));
        return Ok(Result.Ok(message: restored ? "恢复成功" : "角色不存在或未删除，无需恢复"));
    }
}
