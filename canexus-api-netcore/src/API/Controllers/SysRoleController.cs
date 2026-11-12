
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>角色管理接口：分页、下拉简项、详情、增删改与恢复、菜单授权。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/role")]
[Authorize]
public class SysRoleController(ISender sender) : ControllerBase
{
    /// <summary>分页查询角色（keyword 匹配角色名称 / 角色权限字符；status 精确）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemRoleList)]
    public async Task<IActionResult> Page([FromQuery] RolePageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryPaged(request), cancellationToken)));
    }

    /// <summary>获取全部启用角色（不分页，含超管角色，用于用户分配角色下拉）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>角色简项列表。</returns>
    [HttpGet("all")]
    [RequirePermission(PermissionDefinitions.SystemRoleList)]
    public async Task<IActionResult> All(CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryDropdown(), cancellationToken)));
    }

    /// <summary>获取角色详情（含授权菜单 Id 集合，前端用作菜单授权回显）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>角色详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemRoleQuery)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new RoleQueryDetail(id), cancellationToken)));
    }

    /// <summary>创建角色（roleKey 唯一且不可使用保留值 super_admin；菜单授权全删全插）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示角色写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemRoleAdd)]
    [OperLog("角色管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] RoleInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new RoleCreateCommand(request), cancellationToken);
        return Ok(Result.Ok(affected, message: "创建成功"));
    }

    /// <summary>更新角色（内置 super_admin 角色整体拒绝编辑）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemRoleEdit)]
    [OperLog("角色管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] RoleInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new RoleUpdateCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "更新成功"));
    }

    /// <summary>删除角色（内置 super_admin 角色拒绝删除；已被用户引用拒绝删除）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemRoleRemove)]
    [OperLog("角色管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new RoleDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }

    /// <summary>恢复角色（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemRoleRestore)]
    [OperLog("角色管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new RoleRestoreCommand(id, version), cancellationToken);
        return Ok(Result.Ok(restored, message: restored ? "恢复成功" : "角色不存在或未删除，无需恢复"));
    }

    /// <summary>修改角色状态（独立启停：仅提交状态字段；停用后权限缓存由监听器全量失效即时生效；super_admin 拒绝启停）。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">状态请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/status")]
    [RequirePermission(PermissionDefinitions.SystemRoleEdit)]
    [OperLog("角色管理", OperBusinessType.Update)]
    public async Task<IActionResult> UpdateStatus(Guid id, Guid version, [FromBody] RoleStatusRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new RoleUpdateStatusCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "状态修改成功"));
    }

    /// <summary>导出角色列表（xlsx；复用列表同口径筛选，行数上限 10000）。</summary>
    /// <param name="request">角色筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>Excel 文件流。</returns>
    [HttpGet("export")]
    [RequirePermission(PermissionDefinitions.SystemRoleExport)]
    [OperLog("角色管理", OperBusinessType.Export)]
    public async Task<IActionResult> Export([FromQuery] RolePageRequest request, CancellationToken cancellationToken = default)
    {
        var file = await sender.Send(new RoleExportQuery(request), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

}
