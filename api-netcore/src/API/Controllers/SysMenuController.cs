
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>菜单管理接口：树查询、详情、增删改（目录 / 菜单 / 按钮统一维护）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/menu")]
[Authorize]
public class SysMenuController(ISender sender) : ControllerBase
{
    /// <summary>查询菜单树（keyword 匹配菜单名称 / 权限标识；status 精确；含按钮类型）。</summary>
    /// <param name="keyword">关键字。</param>
    /// <param name="status">状态。</param>
    /// <returns>菜单树。</returns>
    [HttpGet("tree")]
    [RequirePermission("system:menu:list")]
    public async Task<IActionResult> Tree([FromQuery] string? keyword, [FromQuery] Domain.Shared.Enums.CommonStatus? status)
    {
        return Ok(Result.Ok(await sender.Send(new GetMenuTreeQuery(keyword, status))));
    }

    /// <summary>获取菜单详情。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <returns>菜单详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("system:menu:query")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await sender.Send(new GetMenuDetailQuery(id))));
    }

    /// <summary>创建菜单（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新菜单 Id。</returns>
    [HttpPost]
    [RequirePermission("system:menu:add")]
    [OperLog("菜单管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] MenuCreateRequest request)
    {
        var id = await sender.Send(new CreateMenuCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
    }

    /// <summary>更新菜单（父级不可选自身或自己的后代）。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:menu:edit")]
    [OperLog("菜单管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MenuUpdateRequest request)
    {
        await sender.Send(new UpdateMenuCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除菜单（存在子菜单或已被角色引用时拒绝）。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:menu:remove")]
    [OperLog("菜单管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await sender.Send(new DeleteMenuCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
