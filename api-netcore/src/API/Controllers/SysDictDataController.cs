namespace API.Controllers;

using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Dicts;
using API.Authorization;
using API.Logging;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>字典数据管理接口：分页、按类型下拉、增删改（同类型下标签 / 键值唯一）。</summary>
[ApiController]
[Route("api/system/dict-data")]
[Authorize]
public class SysDictDataController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>构造函数。</summary>
    /// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
    public SysDictDataController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>分页查询字典数据（dictType 精确、keyword 模糊匹配标签 / 键值）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:dict:list")]
    public async Task<IActionResult> Page([FromQuery] DictDataPageRequest request)
    {
        return Ok(Result.Ok(await _sender.Send(new GetDictDataPageQuery(request))));
    }

    /// <summary>按字典类型查询启用状态的字典数据（走缓存，写操作后立即刷新）。</summary>
    /// <param name="dictType">字典类型。</param>
    /// <returns>字典数据简项列表。</returns>
    [HttpGet("type/{dictType}")]
    [RequirePermission("system:dict:query")]
    public async Task<IActionResult> GetByType(string dictType)
    {
        return Ok(Result.Ok(await _sender.Send(new GetDictDataByTypeQuery(dictType))));
    }

    /// <summary>创建字典数据（所属类型必须存在；同类型下标签 / 键值唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新字典数据 Id。</returns>
    [HttpPost]
    [RequirePermission("system:dict:add")]
    [OperLog("字典数据", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DictDataCreateRequest request)
    {
        var id = await _sender.Send(new CreateDictDataCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
    }

    /// <summary>更新字典数据（类型可迁移，迁移后新旧类型缓存均失效）。</summary>
    /// <param name="id">字典数据 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:dict:edit")]
    [OperLog("字典数据", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] DictDataUpdateRequest request)
    {
        await _sender.Send(new UpdateDictDataCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除字典数据。</summary>
    /// <param name="id">字典数据 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:dict:remove")]
    [OperLog("字典数据", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteDictDataCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
