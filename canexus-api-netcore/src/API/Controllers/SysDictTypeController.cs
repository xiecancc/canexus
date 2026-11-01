
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using Application.Contracts.Queries;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>字典类型管理接口：分页、增删改（dictType 唯一且不可变更）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[Route("api/system/dict-type")]
[Authorize]
public class SysDictTypeController(ISender sender) : ControllerBase
{
    /// <summary>分页查询字典类型（keyword 模糊匹配字典名称 / 字典类型）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:dict:list")]
    public async Task<IActionResult> Page([FromQuery] DictTypePageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new DictTypeQueryPaged(request))));
    }

    /// <summary>创建字典类型（dictType 唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新字典类型 Id。</returns>
    [HttpPost]
    [RequirePermission("system:dict:add")]
    [OperLog("字典类型", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DictTypeCreateRequest request)
    {
        await sender.Send(new DictTypeCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>更新字典类型（dictType 不可变更，与原值不同则拒绝）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:dict:edit")]
    [OperLog("字典类型", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] DictTypeUpdateRequest request)
    {
        await sender.Send(new DictTypeUpdateCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除字典类型（存在字典数据时拒绝）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="request">删除请求（含乐观锁版本号）。</param>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:dict:remove")]
    [OperLog("字典类型", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DictTypeDeleteRequest request)
    {
        await sender.Send(new DictTypeDeleteCommand(id, request.Version));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
