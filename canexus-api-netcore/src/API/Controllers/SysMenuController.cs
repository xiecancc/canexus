
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>菜单管理接口：树查询、详情、增删改与恢复（目录 / 菜单 / 按钮统一维护）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/menu")]
[Authorize]
public class SysMenuController(ISender sender) : ControllerBase
{
    /// <summary>查询菜单树（keyword 匹配菜单名称 / 权限标识；status 精确；含按钮类型）。</summary>
    /// <param name="keyword">关键字。</param>
    /// <param name="status">状态。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>菜单树。</returns>
    [HttpGet("tree")]
    [RequirePermission(PermissionDefinitions.SystemMenuList)]
    public async Task<IActionResult> Tree([FromQuery] string? keyword, [FromQuery] Domain.Shared.Enums.CommonStatus? status, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new MenuQueryTree(keyword, status), cancellationToken));
    }

    /// <summary>获取菜单详情。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>菜单详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemMenuQuery)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new MenuQueryDetail(id), cancellationToken));
    }

    /// <summary>创建菜单（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示菜单写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemMenuAdd)]
    [OperLog("菜单管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] MenuInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new MenuCreateCommand(request), cancellationToken);
        return this.OkData(affected, "创建成功");
    }

    /// <summary>更新菜单（父级不可选自身或自己的后代）。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemMenuEdit)]
    [OperLog("菜单管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] MenuInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new MenuUpdateCommand(id, version, request), cancellationToken);
        return this.OkData(affected, "更新成功");
    }

    /// <summary>删除菜单（存在子菜单或已被角色引用时拒绝）。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemMenuRemove)]
    [OperLog("菜单管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new MenuDeleteCommand(id, version), cancellationToken);
        return this.OkData(affected, "删除成功");
    }

    /// <summary>恢复菜单（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemMenuRestore)]
    [OperLog("菜单管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new MenuRestoreCommand(id, version), cancellationToken);
        return this.OkData(restored, message: restored ? "恢复成功" : "菜单不存在或未删除，无需恢复");
    }
}
